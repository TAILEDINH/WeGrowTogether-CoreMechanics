using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization; // Thêm thư viện để định dạng số hàng nghìn
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace WeGrowTogether.Core
{
    /// <summary>
    /// WaveManager: Bộ não điều khiển các đợt tấn công và chuyển cảnh.
    /// Bản cập nhật Đột phá tích hợp hệ thống Victory & Defeat HUD:
    /// - ⏳ CHUYỂN CẢNH TRÌ HOÃN (DELAYED LOBBY EXIT): Khi thắng hoặc thua, giữ nguyên bối cảnh trận đấu để hiện bảng Victory/Defeat trong 2 giây rồi mới thoát ra sảnh Lobby!
    /// - 🔍 QUÉT TÌM INACTIVE POPUP: Tự động dò tìm VictoryPopupUI và DefeatPopupUI kể cả khi tắt Object từ đầu!
    /// - 🔥 PHÒNG THỦ 3 LỚP (TRANG BỊ GIÁP CHỐNG TÀNG HÌNH): Reset cưỡng bức Animator, SpriteRenderer và trục Z = 0 ngay khi quái ra khỏi Pool!
    /// - 🔥 SINH QUÁI THEO VÙNG (AREA SPAWN): Quái vật được sinh ngẫu nhiên trong một phạm vi không gian, tránh dẫm chân chồng chất lên nhau.
    /// - 🔥 SINH QUÁI THEO NHÓM (SQUAD BATCHING): Sinh quái cực nhanh theo nhóm nhiều con một lúc để tăng độ dồn dập, kịch tính!
    /// - 👑 ĐỢT SĂN BOSS HAI GIAI ĐOẠN (BOSS WAVE PHASES): Sinh quái thường trước để farm vàng, diệt sạch vệ binh mới cho Boss xuất trận!
    /// - ❄️ TIỀN KHỞI TẠO (AUTO PRE-WARM): Khởi tạo sẵn quái vật ẩn dưới map để triệt tiêu hoàn toàn giật lag lúc bắt đầu Wave trên bản Build!
    /// - ⚡ SLIDER ZERO-VALUE FIX: Triệt tiêu hoàn toàn vệt trắng thừa ở đầu Slider khi giá trị về 0 bằng cách ẩn fillRect/handleRect ngay lập tức.
    /// </summary>
    public class WaveManager : MonoBehaviour
    {
        public static WaveManager Instance { get; private set; }
        [Header("--- TUTORIAL CONNECTION ---")]
        public TutorialManager tutorialManager;

        [Header("Camera Zoom")]
        public CameraZoomController camZoom; // Khai báo để WaveManager nhận diện script Zoom

        [Header("Cấu hình quái vật")]
        public EnemyData[] enemyPool;
        public Transform spawnPoint;

        [Header("Cấu hình Vùng Sinh Quái")]
        [Tooltip("Độ lệch ngẫu nhiên tối đa trên trục X khi sinh quái (Ví dụ: 1.5)")]
        public float spawnOffsetXRange = 1.5f;
        [Tooltip("Độ lệch ngẫu nhiên tối đa trên trục Y khi sinh quái (Ví dụ: 0.5 để quái không bị bay quá cao khỏi mặt đất)")]
        public float spawnOffsetYRange = 0.5f;

        [Header("Cấu hình Nhịp Độ Sinh Quái")]
        [Tooltip("Số lượng quái vật sinh ra đồng thời trong mỗi đợt (Ví dụ: 3 con cùng lúc)")]
        public int spawnBatchSize = 3;
        [Tooltip("Khoảng thời gian giãn cách giữa các đợt sinh quái (Ví dụ: 1.2 giây một đợt)")]
        public float spawnInterval = 1.2f;
        [Tooltip("Hệ số scaling số lượng quái theo Wave. Count = 10 + Floor(Wave * hệ số). Càng lớn quái càng đông nhanh.")]
        public float enemyCountScalingPerWave = 3f;

        [Header("Cấu hình Boss")]
        // 🔥 Thêm biến bossSpawnPoint để tách điểm sinh cho Boss
        [Tooltip("Vị trí dành riêng cho Boss. Nếu bỏ trống, hệ thống sẽ dùng chung Spawn Point của quái thường.")]
        public Transform bossSpawnPoint;
        public EnemyData[] miniBossPool;
        public EnemyData[] megaBossPool;

        [Header("Trạng thái Wave")]
        public int currentWave = 1;
        public GameObject startButtonToHide;

        [Header("Giao diện UI")]
        public GameObject waveProgressUI;
        public Slider waveSlider;
        public TextMeshProUGUI waveTitleText;
        public float sliderSmoothSpeed = 5f;

        // Danh sách quản lý quái vật đang sống trên sân
        private List<GameObject> liveEnemies = new List<GameObject>();

        private bool isWaveActive = false;
        private bool isSpawning = false;
        private float targetSliderValue = 1f;

        // 🔥 CHỐT KIỂM TRA TRẬN ĐẤU CHO ANH HÙNG
        public bool IsWaveActive => isWaveActive;

        /// <summary>Wave kết thúc gần nhất có phải do bại trận (Castle bị phá hủy) hay không.</summary>
        public bool LastWaveWasDefeat { get; private set; }

        private List<IWaveObserver> observers = new List<IWaveObserver>();
        public void RegisterObserver(IWaveObserver obs) { if (!observers.Contains(obs)) observers.Add(obs); }

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }

            Debug.Log("<color=white>🛡️ [HỆ THỐNG] WaveManager đã thức tỉnh và sẵn sàng nhận lệnh!</color>");
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void Start()
        {
            if (waveProgressUI != null) waveProgressUI.SetActive(false);

            // Wire tách rời cho PopulationSystem: cung cấp trạng thái defeat qua hook (không tham chiếu ngược).
            PopulationSystem.LastWaveDefeatProvider = () => LastWaveWasDefeat;

            // TỰ ĐỘNG KẾT NỐI: Tìm tất cả lính đang đứng trên sảnh (nếu có)
            RefreshHeroConnections();

            // 🔥 KHỞI ĐỘNG CHỐNG LAG: Thực hiện Pre-Warm nạp sẵn quái vật vào Object Pooler khi vào sảnh chờ
            StartCoroutine(PreWarmEnemiesRoutine());
        }

        /// <summary>
        /// Tiến trình tiền khởi tạo quái vật để triệt tiêu hoàn toàn giật lag khung hình khi spawn lần đầu tiên
        /// </summary>
        private IEnumerator PreWarmEnemiesRoutine()
        {
            yield return new WaitForSeconds(0.5f); // Chờ cho SimpleObjectPooler sẵn sàng

            if (SimpleObjectPooler.Instance != null)
            {
                Debug.Log("<color=cyan>❄️ [PRE-WARM] Bắt đầu nạp sẵn quái vật để tăng tốc hiệu năng bản Build...</color>");

                // Khởi tạo sẵn mỗi loại quái thường khoảng 6 con
                if (enemyPool != null)
                {
                    foreach (var enemy in enemyPool)
                    {
                        if (enemy != null && enemy.prefab != null)
                        {
                            SimpleObjectPooler.Instance.PreWarm(enemy.prefab, 6);
                            yield return null; // Trả khung hình tránh đơ game
                        }
                    }
                }

                // Khởi tạo sẵn mỗi loại Boss khoảng 1 con
                if (miniBossPool != null)
                {
                    foreach (var boss in miniBossPool)
                    {
                        if (boss != null && boss.prefab != null)
                        {
                            SimpleObjectPooler.Instance.PreWarm(boss.prefab, 1);
                        }
                    }
                }

                Debug.Log("<color=green>✔️ [PRE-WARM COMPLETE] Đã nạp sẵn quái vật hoàn chỉnh! Game mượt 100%.</color>");
            }
        }

        /// <summary>
        /// Số vàng thực tế nhặt được từ xác quái rơi ra trong Wave hiện tại
        /// </summary>
        public double GoldEarnedThisWave { get; set; } = 0;

        public int GetLiveEnemyCount() => liveEnemies.Count;

        // Thêm phương thức để quái vật trực tiếp gọi mà không cần qua Reflection
        public void AddTrackedGold(double amount)
        {
            GoldEarnedThisWave += amount;
        }

        public void RefreshHeroConnections()
        {
            var heroes = FindObjectsByType<HeroController>(FindObjectsSortMode.None);
            foreach (var h in heroes)
            {
                h.Init(this);
            }
            Debug.Log($"<color=cyan>📡 [WAVE] Đã thiết lập kết nối với {heroes.Length} anh hùng trên tháp.</color>");
        }

        void Update()
        {
            if (waveSlider != null && waveProgressUI != null && waveProgressUI.activeInHierarchy)
            {
                float currentVal = waveSlider.value;
                float targetVal = targetSliderValue;

                // ⚡ HỆ THỐNG TRƯỢT SIÊU MƯỢT (SMOOTH INTERPOLATION):
                currentVal = Mathf.Lerp(currentVal, targetVal, Time.deltaTime * sliderSmoothSpeed);

                // 🎯 CHỐT CHẶN KHÓA GIÁ TRỊ VỀ ĐÍCH:
                if (Mathf.Abs(currentVal - targetVal) < 0.002f)
                {
                    currentVal = targetVal;
                }

                waveSlider.value = currentVal;

                // 🔥 SỬA LỖI SLIDER TRẮNG (ELIMINATE ZERO-VALUE FILL SLIVER):
                // Khi slider về sát 0, ẩn hoàn toàn fillRect và handleRect để xóa sổ vệt trắng dư thừa ở rìa trái!
                bool showFill = currentVal > 0.002f;
                if (waveSlider.fillRect != null)
                {
                    waveSlider.fillRect.gameObject.SetActive(showFill);
                }
                if (waveSlider.handleRect != null)
                {
                    waveSlider.handleRect.gameObject.SetActive(showFill);
                }
            }
        }

        /// <summary>
        /// Hàm này được gọi từ Button hoặc GameManager để bắt đầu đợt quái mới
        /// </summary>
        public void StartNextWave()
        {
            if (camZoom != null)
            {
                camZoom.StartWaveZoom(); // Ra lệnh Zoom rộng ra
            }

            if (TutorialManager.Instance != null)
            {
                TutorialManager.Instance.CloseAll();
            }
            else
            {
                // Nếu không tìm thấy, thử tìm thủ công trong Scene 1 lần duy nhất
                var tutorial = FindObjectOfType<TutorialManager>();
                if (tutorial != null)
                {
                    tutorial.CloseAll();
                }
            }
            if (WeGrowTogether.Core.CastleUpgradeSystem.Instance != null)
            {
                WeGrowTogether.Core.CastleUpgradeSystem.Instance.SetPanelActive(false);
            }
            if (WeGrowTogether.Core.SpaceFighterUpgradeSystem.Instance != null)
            {
                WeGrowTogether.Core.SpaceFighterUpgradeSystem.Instance.SetPanelActive(false);
            }
            if (WeGrowTogether.Core.SettingsPopupUI.Instance != null)
            {
                WeGrowTogether.Core.SettingsPopupUI.Instance.SetHUDButtonActive(false);
            }

            // Ẩn triệt để bảng Victory trước khi vào trận mới bằng cách tìm kiếm cứu hộ chất lượng cao
            var victoryUI = FindVictoryPopupSystem();
            if (victoryUI != null && victoryUI.victoryPanelRoot != null)
            {
                victoryUI.victoryPanelRoot.SetActive(false);
            }

            // Ẩn triệt để bảng Defeat trước khi vào trận mới
            var defeatUI = FindDefeatPopupSystem();
            if (defeatUI != null && defeatUI.defeatPanelRoot != null)
            {
                defeatUI.defeatPanelRoot.SetActive(false);
            }

            if (isWaveActive)
            {
                Debug.LogWarning("⚠️ [WAVE] Lệnh bị từ chối: Wave đang chạy rồi!");
                return;
            }

            Debug.Log("<color=orange>🚀 [HÀNH ĐỘNG] Đã nhận lệnh StartNextWave thành công!</color>");

            if (enemyPool == null || enemyPool.Length == 0)
            {
                Debug.LogError("🚨 [LỖI] Ô Enemy Pool đang trống! Hãy kéo file EnemyData vào.");
                return;
            }

            if (SimpleObjectPooler.Instance == null)
            {
                Debug.LogError("🚨 [LỖI] Không tìm thấy SimpleObjectPooler! Kiểm tra object [SYSTEMS].");
                return;
            }

            // 🔥 SỬA LỖI CỘNG DỒN: Reset số vàng nhặt được của Wave trước về 0 khi bắt đầu Wave mới!
            GoldEarnedThisWave = 0;

            // 🛡️ RESET BỘ ĐẾM RESILIENCE POINT CỦA WAVE MỚI
            var rm = ResilienceManager.Instance;
            if (rm != null)
            {
                rm.ResetWaveCounter();
            }

            isWaveActive = true;
            isSpawning = true;
            liveEnemies.Clear();

            // Bật nút Pause trên HUD khi Wave chính thức bắt đầu
            if (PauseWavePopupUI.Instance != null)
            {
                PauseWavePopupUI.Instance.SetPauseButtonActive(true);
            }

            if (startButtonToHide != null) startButtonToHide.SetActive(false);
            if (waveProgressUI != null)
            {
                waveProgressUI.SetActive(true);
                waveSlider.value = 1f;
                targetSliderValue = 1f;

                // 🔥 PHỤC HỒI HIỂN THỊ FILL VÀ HANDLE KHI BẮT ĐẦU TRẬN ĐẤU MỚI:
                if (waveSlider.fillRect != null)
                {
                    waveSlider.fillRect.gameObject.SetActive(true);
                }
                if (waveSlider.handleRect != null)
                {
                    waveSlider.handleRect.gameObject.SetActive(true);
                }

                if (waveTitleText != null)
                    waveTitleText.text = $"{currentWave}";
            }

            foreach (var obs in observers) { if (obs != null) obs.OnWaveStarted(currentWave); }

            StartCoroutine(SpawnRoutine());
        }

        /// <summary>
        /// Tiến trình sinh quái vật nâng cấp:
        /// - Tại Wave thường: Sinh quái thường như cũ.
        /// - Tại Wave Boss: Sinh quái thường vệ binh trước, khi người chơi quét sạch thì Boss mới xuất kích!
        /// </summary>
        private IEnumerator SpawnRoutine()
        {
            bool isBossWave = (currentWave % 5 == 0);
            bool isMegaWave = (currentWave % 10 == 0);

            // GIAI ĐOẠN 1: Sinh quái thường (Áp dụng cho cả Wave thường và Wave Boss để kiếm Vàng)
            int totalToSpawn = Mathf.Min(10 + Mathf.FloorToInt(currentWave * enemyCountScalingPerWave), 500);
            int spawnedCount = 0;

            if (isBossWave)
            {
            }
            else
            {
            }

            while (spawnedCount < totalToSpawn)
            {
                // Lấy số lượng quái sẽ sinh trong đợt nhóm này
                int batchToSpawn = Mathf.Min(spawnBatchSize, totalToSpawn - spawnedCount);

                for (int b = 0; b < batchToSpawn; b++)
                {
                    SpawnSingleEnemy();
                    spawnedCount++;
                    targetSliderValue = 1.0f - ((float)spawnedCount / totalToSpawn);
                }

                // Đợi đợt sinh tiếp theo thay vì sinh từng con nhỏ giọt
                yield return new WaitForSeconds(spawnInterval);
            }

            // 🔥 ĐÃ SINH XONG QUÁI CUỐI CÙNG: Chỉ đặt targetSliderValue = 0f. 
            // KHÔNG ép cứng waveSlider.value = 0f hay ẩn fillRect ở đây nữa để hàm Update() tự trượt mượt mà về đích và tự tắt vệt trắng!
            targetSliderValue = 0f;

            // GIAI ĐOẠN 2: Xử lý riêng biệt đối với Wave có Boss
            if (isBossWave)
            {
                while (liveEnemies.Count > 0)
                {
                    yield return new WaitForSeconds(0.5f);
                }

                targetSliderValue = 0f;
                yield return new WaitForSeconds(1.5f);

                SpawnBoss(isMegaWave);
            }

            isSpawning = false;
            CheckWaveCompletion();
        }

        /// <summary>
        /// Hàm nội bộ giúp tính toán vị trí sinh quái ngẫu nhiên trong vùng chỉ định
        /// </summary>
        private Vector3 GetRandomSpawnPosition()
        {
            if (spawnPoint == null) return Vector3.zero;

            // Offset ngẫu nhiên để quái dàn hàng ra, không bị dính vào một điểm duy nhất
            float offsetX = UnityEngine.Random.Range(-spawnOffsetXRange, spawnOffsetXRange);
            float offsetY = UnityEngine.Random.Range(-spawnOffsetYRange, spawnOffsetYRange);

            // 🔥 PHÒNG THỦ LỚP 1: Ép cứng tọa độ Z về 0 tuyệt đối ngay từ nguồn sinh
            return new Vector3(spawnPoint.position.x + offsetX, spawnPoint.position.y + offsetY, 0f);
        }

        private void SpawnSingleEnemy()
        {
            if (enemyPool.Length == 0) return;

            Vector3 spawnPos = GetRandomSpawnPosition();
            EnemyData d = enemyPool[UnityEngine.Random.Range(0, enemyPool.Length)];
            GameObject obj = SimpleObjectPooler.Instance.GetFromPool(d.prefab, spawnPos, Quaternion.identity);

            if (obj != null)
            {
                // 🔥 PHÒNG THỦ LỚP 2: Ép buộc phục hồi cấu hình hiển thị và Scale ngay khi lôi ra khỏi Pool
                // Chặn đứng hoàn toàn nguy cơ quái bị tàng hình do Animator ghi đè giá trị cũ khi chết!
                obj.transform.localScale = Vector3.one;
                obj.transform.position = spawnPos;

                var renderers = obj.GetComponentsInChildren<SpriteRenderer>(true);
                foreach (var r in renderers)
                {
                    r.enabled = true;
                    r.color = Color.white;
                }

                Animator animator = obj.GetComponent<Animator>();
                if (animator == null) animator = obj.GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    animator.Rebind();
                    animator.Update(0f);
                }

                if (!liveEnemies.Contains(obj)) liveEnemies.Add(obj);

                double hp = GameBalancer.CalculateMonsterHP(d.baseHP, currentWave);
                double dmg = GameBalancer.CalculateMonsterDamage(d.baseDamage, currentWave);
                double gold = GameBalancer.CalculateMonsterGold(d.baseGoldReward, currentWave);

                var mc = obj.GetComponentInChildren<MonsterController>();
                if (mc != null)
                {
                    // 🔥 THÊM MỚI: Truyền thêm d (EnemyData) và currentWave vào hàm Setup để tính EXP
                    mc.Setup(hp, dmg, gold, d.baseSpeed, this, d, currentWave);
                }
                else
                {
                    Debug.LogError($"🚨 [LỖI] Prefab {obj.name} thiếu script MonsterController!");
                }
            }
        }

        /// <summary>
        /// Triệu hồi Boss chiến trường (Mini Boss / Mega Boss)
        /// Tối ưu hóa sửa lỗi NullReferenceException an toàn tuyệt đối.
        /// </summary>
        private void SpawnBoss(bool isMega)
        {
            EnemyData[] pool = isMega ? megaBossPool : miniBossPool;

            // 1. Kiểm tra an toàn cho mảng Pool Boss cấu hình
            if (pool == null || pool.Length == 0)
            {
                Debug.LogWarning($"⚠️ [WAVE MANAGER] Mảng {(isMega ? "megaBossPool" : "miniBossPool")} đang trống hoặc NULL! Hệ thống tự động chuyển hướng sinh quái thường thay thế.");
                SpawnSingleEnemy();
                return;
            }

            // 2. 🔥 Áp dụng Logic Chọn Vị Trí Spawn Cho Boss
            Vector3 spawnPos = Vector3.zero;

            // Ưu tiên dùng bossSpawnPoint nếu đã được kéo thả trong Inspector
            if (bossSpawnPoint != null)
            {
                spawnPos = new Vector3(bossSpawnPoint.position.x, bossSpawnPoint.position.y, 0f);
            }
            // Nếu quên cấu hình bossSpawnPoint, lấy tạm spawnPoint của quái thường
            else if (spawnPoint != null)
            {
                Debug.LogWarning("⚠️ [WAVE MANAGER] 'Boss Spawn Point' chưa được gán. Đang dùng chung điểm spawn của quái thường!");
                spawnPos = new Vector3(spawnPoint.position.x, spawnPoint.position.y, 0f);
            }
            else
            {
                Debug.LogError("🚨 [WAVE MANAGER LỖI CHÍ MẠNG] Không có bất kỳ Spawn Point nào (Boss hay Thường) được gán. " +
                    "Hệ thống sẽ lấy tọa độ Vector3.zero để tránh sập game.");
            }

            // 3. Chọn ngẫu nhiên cấu hình quái và kiểm tra an toàn dữ liệu
            EnemyData d = pool[UnityEngine.Random.Range(0, pool.Length)];
            if (d == null)
            {
                Debug.LogError("🚨 [WAVE MANAGER] Phần tử EnemyData được chọn ngẫu nhiên trong Boss Pool bị NULL! Vui lòng kiểm tra lại danh sách Boss trong Inspector.");
                SpawnSingleEnemy();
                return;
            }

            if (d.prefab == null)
            {
                Debug.LogError($"🚨 [WAVE MANAGER] Prefab của Boss '{d.name}' đang bị NULL! Vui lòng kéo thả Prefab tương ứng vào ScriptableObject của Boss này.");
                SpawnSingleEnemy();
                return;
            }

            // 4. Kiểm tra an toàn bộ Pooler
            if (SimpleObjectPooler.Instance == null)
            {
                Debug.LogError("🚨 [WAVE MANAGER] Không tìm thấy SimpleObjectPooler.Instance! Không thể lấy Boss từ Pool.");
                return;
            }

            GameObject obj = SimpleObjectPooler.Instance.GetFromPool(d.prefab, spawnPos, Quaternion.identity);

            if (obj != null)
            {
                // 🔥 PHÒNG THỦ LỚP 3: Áp dụng dọn dẹp hiển thị tương tự cho Boss
                obj.transform.localScale = Vector3.one;
                obj.transform.position = spawnPos;

                var renderers = obj.GetComponentsInChildren<SpriteRenderer>(true);
                foreach (var r in renderers)
                {
                    r.enabled = true;
                    r.color = Color.white;
                }

                Animator animator = obj.GetComponent<Animator>();
                if (animator == null) animator = obj.GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    animator.Rebind();
                    animator.Update(0f);
                }

                if (!liveEnemies.Contains(obj)) liveEnemies.Add(obj);

                // Sử dụng trực tiếp hệ thống GameBalancer thống nhất, loại bỏ hoàn toàn các biến multiplier nhân chồng chéo cục bộ
                double hp = GameBalancer.CalculateBossHP(d.baseHP, currentWave, isMega);
                double dmg = GameBalancer.CalculateBossDamage(d.baseDamage, currentWave, isMega);
                double gold = GameBalancer.CalculateBossGold(d.baseGoldReward, currentWave, isMega);

                var bc = obj.GetComponentInChildren<BossController>();
                if (bc != null)
                {
                    // 🛡️ GÁN THƯỞNG RESILIENCE POINT THEO LOẠI BOSS: Mini = 1 RP, Mega = 10 RP
                    var rm = ResilienceManager.Instance;
                    if (rm != null)
                    {
                        bc.resilienceReward = isMega ? rm.pointsPerMegaBoss : rm.pointsPerMiniBoss;
                    }
                    else
                    {
                        bc.resilienceReward = isMega ? 10.0 : 1.0;
                    }

                    // 🔥 THÊM MỚI: Truyền thêm d (EnemyData) và currentWave vào hàm Setup của Boss
                    bc.Setup(hp, dmg, gold, d.baseSpeed * 0.7f, this, d, currentWave);
                }
                else
                {
                    var mc = obj.GetComponentInChildren<MonsterController>();
                    if (mc != null)
                    {
                        mc.isBoss = true;
                        // 🔥 THÊM MỚI: Truyền thêm d (EnemyData) và currentWave vào hàm Setup của Monster giả Boss
                        mc.Setup(hp, dmg, gold, d.baseSpeed * 0.7f, this, d, currentWave);
                    }
                }
            }
        }

        public void UnregisterEnemy(GameObject obj)
        {
            if (obj == null) return;

            GameObject pooledRoot = obj;
            while (pooledRoot != null)
            {
                if (pooledRoot.GetComponent<PooledObjectMarker>() != null) break;
                if (pooledRoot.transform.parent != null)
                    pooledRoot = pooledRoot.transform.parent.gameObject;
                else
                    break;
            }

            if (liveEnemies.Contains(pooledRoot)) liveEnemies.Remove(pooledRoot);
            if (SimpleObjectPooler.Instance != null) SimpleObjectPooler.Instance.ReturnToPool(pooledRoot);
            CheckWaveCompletion();
        }

        private void CheckWaveCompletion()
        {
            if (!isSpawning && liveEnemies.Count == 0 && isWaveActive)
            {
                EndWave();
            }
        }

        /// <summary>
        /// Kích hoạt chu kỳ kết thúc Wave đấu khi Thắng.
        /// </summary>
        private void EndWave()
        {
            StartCoroutine(EndWaveDelayedRoutine());
        }

        /// <summary>
        /// 🔥 TIẾN TRÌNH TRÌ HOÃN CHUYỂN CẢNH KHI THẮNG TRẬN ĐẤU (IN-WAVE VICTORY POPUP ROUTINE)
        /// </summary>
        private IEnumerator EndWaveDelayedRoutine()
        {
            LastWaveWasDefeat = false; // Wave thắng
            if (camZoom != null)
            {
                camZoom.EndWaveZoom(); // Ra lệnh Thu nhỏ lại
            }
            // 🔥 ẨN TIẾN TRÌNH NGAY LẬP TỨC: Giúp màn hình sạch đẹp hoàn toàn khi hiện bảng Victory
            if (waveProgressUI != null)
            {
                waveSlider.value = 0f;
                targetSliderValue = 0f;
                if (waveSlider.fillRect != null) waveSlider.fillRect.gameObject.SetActive(false);
                if (waveSlider.handleRect != null) waveSlider.handleRect.gameObject.SetActive(false);
                waveProgressUI.SetActive(false);
            }
            // 👉 DÁN 2 DÒNG NÀY VÀO ĐÂY ĐỂ TẮT SCROLL VIEW LUÔN:
            var hotbar = UnityEngine.Object.FindFirstObjectByType<HotbarAutoSpawner>();
            if (hotbar != null && hotbar.scrollViewRoot != null) hotbar.scrollViewRoot.SetActive(false);
            // 1. Duy trì trạng thái isWaveActive = true để giữ nguyên camera chiến trường, chưa bật lại Lobby HUD
            isWaveActive = true;

            // 🔥 QUÉT SIÊU NHẠY CHỐNG TÀNG HÌNH: Tìm kiếm VictoryPopupUI kể cả khi nó đang bị tắt (Inactive)
            var victoryUI = FindVictoryPopupSystem();

            float delay = 2f;
            if (victoryUI != null)
            {
                delay = victoryUI.autoCloseDelay;
                // 2. Kích hoạt bảng chúc mừng vinh danh ngay khi đang trong trận sòng phẳng!
                victoryUI.ShowVictory(currentWave);
                Debug.Log("<color=green>🏆 [WAVE MANAGER] Đã gọi kích hoạt VictoryPopupUI thành công trong trận đấu!</color>");
            }
            else
            {
                Debug.LogError("🚨 [WAVE MANAGER] Không tìm thấy VictoryPopupUI trong toàn bộ Scene!");
            }

            // Tắt nhanh nút tạm dừng HUD để tránh người chơi spam ESC/Pause khi đang hiện bảng Victory
            if (PauseWavePopupUI.Instance != null)
            {
                PauseWavePopupUI.Instance.SetPauseButtonActive(false);
            }

            // 3. Đóng băng thời gian chờ đúng số giây cấu hình trên Inspector (Ví dụ: 2 giây)
            yield return new WaitForSeconds(delay);

            // 4. Ẩn bảng Victory hoành tráng đi sòng phẳng
            if (victoryUI != null)
            {
                victoryUI.HideVictory();
            }

            // 5. 🔥 BÂY GIỜ MỚI CHÍNH THỨC THOÁT TRẬN ĐẤU QUAY VỀ LOBBY SẢNH CHỜ!
            if (WeGrowTogether.Core.CastleUpgradeSystem.Instance != null)
            {
                WeGrowTogether.Core.CastleUpgradeSystem.Instance.SetPanelActive(true);
            }
            if (WeGrowTogether.Core.SpaceFighterUpgradeSystem.Instance != null)
            {
                WeGrowTogether.Core.SpaceFighterUpgradeSystem.Instance.SetPanelActive(true);
            }
            if (WeGrowTogether.Core.SettingsPopupUI.Instance != null)
            {
                WeGrowTogether.Core.SettingsPopupUI.Instance.SetHUDButtonActive(true);
            }

            isWaveActive = false; // Chính thức tắt cờ để mở khóa sảnh sòng phẳng
            Debug.Log("<color=green>🏆 [VICTORY] Hoàn tất hiển thị Victory 2s trong Wave! Đã quay lại Lobby sảnh chờ.</color>");

            if (startButtonToHide != null) startButtonToHide.SetActive(true);

            // Gửi thông báo hoàn thành đến các hệ thống Observer (như GameManager để hồi đầy HP/Mana và lưu game)
            foreach (var obs in observers)
            {
                if (obs != null)
                {
                    obs.OnWaveFinished(currentWave);
                }
            }

            currentWave++;

            // Lưu game sòng phẳng để niêm phong két sắt tài sản mới đạt được
            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.SaveGame();
            }
        }

        /// <summary>
        /// Bộ quét tìm kiếm VictoryPopupUI thông minh xuyên màn đêm
        /// </summary>
        private VictoryPopupUI FindVictoryPopupSystem()
        {
            if (VictoryPopupUI.Instance != null) return VictoryPopupUI.Instance;
            return UnityEngine.Object.FindFirstObjectByType<VictoryPopupUI>(FindObjectsInactive.Include);
        }

        /// <summary>
        /// Bộ quét tìm kiếm DefeatPopupUI thông minh xuyên màn đêm
        /// </summary>
        private DefeatPopupUI FindDefeatPopupSystem()
        {
            if (DefeatPopupUI.Instance != null) return DefeatPopupUI.Instance;
            return UnityEngine.Object.FindFirstObjectByType<DefeatPopupUI>(FindObjectsInactive.Include);
        }

        /// <summary>
        /// 🔥 GIAO THỨC CƯỠNG BỨC DỪNG TRẬN SÒNG PHẲNG (FORCE STOP WAVE):
        /// Được tối ưu hóa trì hoãn 2 giây để hiện bảng Defeat ngay trong trận y chang Victory!
        /// </summary>
        public void ForceStopWave()
        {
            // Kiểm tra xem đây có phải là bại trận thực sự (Castle HP về 0) hay không
            bool isDefeat = (CastleController.Instance != null && CastleController.Instance.currentHP <= 0);
            LastWaveWasDefeat = isDefeat;

            if (isDefeat)
            {
                StopAllCoroutines(); // Dừng spawn quái ngay lập tức
                StartCoroutine(ForceStopWaveDelayedRoutine());
            }
            else
            {
                // Instant cancellation / manual stop (Ví dụ: Từ Pause menu hoặc click Exit)
                StopAllCoroutines();
                ExecuteInstantForceStop();
            }
        }

        private IEnumerator ForceStopWaveDelayedRoutine()
        {
            isSpawning = false;
            isWaveActive = true; // Giữ nguyên bối cảnh tháp chiến đấu, chưa bật lại Lobby HUD

            // 🔥 BỔ SUNG LỆNH THU CAMERA KHI THUA TRẬN:
            if (camZoom != null)
            {
                camZoom.EndWaveZoom();
            }

            // 🔥 ẨN TIẾN TRÌNH NGAY LẬP TỨC KHI BẠI TRẬN:
            if (waveProgressUI != null)
            {
                waveSlider.value = 0f;
                targetSliderValue = 0f;
                if (waveSlider.fillRect != null) waveSlider.fillRect.gameObject.SetActive(false);
                if (waveSlider.handleRect != null) waveSlider.handleRect.gameObject.SetActive(false);
                waveProgressUI.SetActive(false);
            }

            // 👉 DÁN 2 DÒNG NÀY VÀO ĐÂY ĐỂ TẮT SCROLL VIEW LUÔN:
            var hotbar = UnityEngine.Object.FindFirstObjectByType<HotbarAutoSpawner>();
            if (hotbar != null && hotbar.scrollViewRoot != null) hotbar.scrollViewRoot.SetActive(false);

            // 1. DỌN SẠCH QUÁI VẬT LẬP TỨC để sảnh chơi sạch đẹp khi hiện bảng bại trận, tránh quái cứ cắn tàu tiếp tục
            ClearAllEnemiesInstant();

            // 2. Tìm kiếm và kích hoạt bảng Defeat báo thua ngay trong trận đấu
            var defeatUI = FindDefeatPopupSystem();
            float delay = 2f;

            if (defeatUI != null)
            {
                delay = defeatUI.autoCloseDelay; // Đọc chỉ số autoCloseDelay đã sửa sòng phẳng!
                defeatUI.ShowDefeat(currentWave);
                Debug.Log("<color=red>💀 [WAVE MANAGER] Đã gọi kích hoạt DefeatPopupUI thành công trong trận đấu!</color>");
            }
            else
            {
                Debug.LogError("🚨 [WAVE MANAGER] Không tìm thấy DefeatPopupUI trong Scene!");
            }

            if (PauseWavePopupUI.Instance != null)
            {
                PauseWavePopupUI.Instance.SetPauseButtonActive(false);
            }

            // 3. Đóng băng đứng lại chờ đúng số giây cấu hình trên DefeatPopupUI (Mặc định 2 giây)
            yield return new WaitForSeconds(delay);

            // 4. Ẩn bảng Defeat đi sòng phẳng
            if (defeatUI != null)
            {
                defeatUI.HideDefeat();
            }

            // 5. 🔥 BÂY GIỜ MỚI CHÍNH THỨC THOÁT TRẬN ĐẤU QUAY VỀ LOBBY SẢNH CHỜ!
            ExecuteInstantForceStop();
        }

        /// <summary>
        /// Thực thi dập tắt và dọn dẹp chiến trường ngay lập tức, chuyển trạng thái về sảnh chờ (Lobby).
        /// </summary>
        private void ExecuteInstantForceStop()
        {
            isWaveActive = false;
            isSpawning = false;

            // 🔥 BỔ SUNG LỆNH THU CAMERA KHI BẤM THOÁT HOẶC KẾT THÚC CƯỠNG BỨC:
            if (camZoom != null)
            {
                camZoom.EndWaveZoom();
            }

            // Đảm bảo ẩn triệt để cả hai bảng popup nếu còn chạy dở
            var victoryUI = FindVictoryPopupSystem();
            if (victoryUI != null) victoryUI.HideVictory();

            var defeatUI = FindDefeatPopupSystem();
            if (defeatUI != null) defeatUI.HideDefeat();

            // 🔥 ĐỒNG BỘ PHỤC HỒI GIAO DIỆN LOBBY KHI THUA/HỦY TRẬN:
            if (WeGrowTogether.Core.CastleUpgradeSystem.Instance != null)
            {
                WeGrowTogether.Core.CastleUpgradeSystem.Instance.SetPanelActive(true);
            }
            if (WeGrowTogether.Core.SpaceFighterUpgradeSystem.Instance != null)
            {
                WeGrowTogether.Core.SpaceFighterUpgradeSystem.Instance.SetPanelActive(true);
            }
            if (WeGrowTogether.Core.SettingsPopupUI.Instance != null)
            {
                WeGrowTogether.Core.SettingsPopupUI.Instance.SetHUDButtonActive(true);
            }

            // 🔥 ĐẠI TU PHI THUYỀN: Sạc đầy 100% HP & Mana chuẩn bị sẵn sàng cho trận sau
            if (CastleController.Instance != null)
            {
                CastleController.Instance.FullHeal();
                Debug.Log("<color=green>✔️ [RECOVERY] Đã sạc đầy HP/Mana phi thuyền sau trận đấu!</color>");
            }

            if (PauseWavePopupUI.Instance != null)
            {
                PauseWavePopupUI.Instance.SetPauseButtonActive(false);
            }

            ClearAllEnemiesInstant();

            if (startButtonToHide != null) startButtonToHide.SetActive(true);
            if (waveProgressUI != null) waveProgressUI.SetActive(false);

            // 🔥 SỬA LỖI TRỤC LỢI KIM CƯƠNG:
            // Chỉ gửi sự kiện OnWaveFinished tới các UI dọn dẹp chiến trường (như PauseWavePopupUI, HeroGridUI).
            // Không gửi sự kiện cho GameManager hoặc các thành phần lưu trữ/nhận thưởng để chặn đứng việc lén cộng Kim Cương.
            foreach (var obs in observers)
            {
                if (obs != null)
                {
                    // Chặn tất cả observer là GameManager hoặc có tên GameManager tránh lách luật nhận thưởng
                    if (obs.GetType().Name.Contains("GameManager"))
                    {
                        continue;
                    }
                    obs.OnWaveFinished(currentWave);
                }
            }

            Debug.Log($"<color=green>✔️ [FORCE STOP COMPLETE] Trở về sảnh chuẩn bị thành công cho Wave {currentWave}!</color>");
        }

        /// <summary>
        /// Dọn sạch tức thời mọi quái vật đang sống trên sân về Pool
        /// </summary>
        private void ClearAllEnemiesInstant()
        {
            if (liveEnemies != null && liveEnemies.Count > 0)
            {
                for (int i = liveEnemies.Count - 1; i >= 0; i--)
                {
                    GameObject enemy = liveEnemies[i];
                    if (enemy != null)
                    {
                        ReturnEnemyToPool(enemy);
                    }
                }
                liveEnemies.Clear();
            }
        }

        private void ReturnEnemyToPool(GameObject enemy)
        {
            if (enemy == null) return;
            if (SimpleObjectPooler.Instance != null)
            {
                SimpleObjectPooler.Instance.ReturnToPool(enemy);
            }
            else
            {
                Destroy(enemy.transform.root.gameObject);
            }
        }

        public Transform GetNearestEnemy(Vector3 pos, float range)
        {
            if (liveEnemies.Count == 0) return null;

            Transform near = null;
            float minDistSqr = range * range;

            for (int i = liveEnemies.Count - 1; i >= 0; i--)
            {
                if (liveEnemies[i] == null || !liveEnemies[i].activeInHierarchy) continue;

                float dx = pos.x - liveEnemies[i].transform.position.x;
                float dy = pos.y - liveEnemies[i].transform.position.y;
                float distSqr = dx * dx + dy * dy;

                if (distSqr < minDistSqr)
                {
                    minDistSqr = distSqr;
                    near = liveEnemies[i].transform;
                }
            }

            return near;
        }

        private static readonly List<GameObject> s_TargetCandidates = new List<GameObject>();

        public Transform GetRandomEnemy(Vector3 pos, float range)
        {
            if (liveEnemies.Count == 0) return null;

            s_TargetCandidates.Clear();
            float rangeSqr = range * range;

            for (int i = liveEnemies.Count - 1; i >= 0; i--)
            {
                GameObject enemy = liveEnemies[i];
                if (enemy == null || !enemy.activeInHierarchy) continue;

                float dx = pos.x - enemy.transform.position.x;
                float dy = pos.y - enemy.transform.position.y;
                float distSqr = dx * dx + dy * dy;

                if (distSqr <= rangeSqr)
                {
                    s_TargetCandidates.Add(enemy);
                }
            }

            if (s_TargetCandidates.Count == 0) return null;
            if (s_TargetCandidates.Count == 1) return s_TargetCandidates[0].transform;

            int randomIndex = UnityEngine.Random.Range(0, s_TargetCandidates.Count);
            return s_TargetCandidates[randomIndex].transform;
        }
    }
}