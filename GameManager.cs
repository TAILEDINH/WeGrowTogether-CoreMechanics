using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace WeGrowTogether.Core
{
    /// <summary>
    /// GameManager: Bộ não điều phối kinh tế và tiến trình Wave.
    /// Tích hợp: Hồi máu phi thuyền, quản lý 2 loại kim cương và tự động nạp dữ liệu từ file Save.
    /// </summary>
    public class GameManager : MonoBehaviour, IWaveObserver
    {
        public static GameManager Instance { get; private set; }

        [Header("--- KINH TẾ (ECONOMY) ---")]
        public double currentGold = 100;
        public int whiteGems = 10;      // Kim cương thường (Màu trắng - Kiếm từ Wave)
        public int orangeGems = 0;      // Kim cương cộng thêm (Màu cam - Quà tặng/Nạp)
        public int maxWhiteGems = 80;   // Giới hạn kim cương trắng có thể chứa

        [Header("--- THAM CHIẾU HỆ THỐNG ---")]
        public WaveManager waveManager;
        public GameUIManager uiManager;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            // 🔥 TỐI ƯU HÓA HIỆU NĂNG BẢN BUILD: Khóa khung hình ở 60 FPS để quái di chuyển mượt mà!
            Application.targetFrameRate = 60;

            // 🔥 ĐỒNG BỘ V-SYNC: Giúp triệt tiêu hoàn toàn giật màn hình (Screen tearing/jittering) trên điện thoại và màn hình PC
            QualitySettings.vSyncCount = 1; // 1 = Đồng bộ với tần số quét của màn hình (mượt và tiết kiệm pin tối đa)

            // 🔥 CỨU CÁNH TREO MÁY: Ép game luôn luôn chạy dưới nền kể cả khi người chơi tab out!
            // Hỗ trợ tuyệt vời cho người chơi muốn cắm chuột treo máy (AFK) hoặc mở nhiều acc clone.
            Application.runInBackground = true;
            Debug.Log("<color=#FF55FF>☕ [GameManager] Đã kích hoạt chạy dưới nền: Application.runInBackground = true</color>");
        }

        private void Start()
        {
            // 1. TÌM KIẾM HỆ THỐNG (Nếu lỡ quên kéo dây trong Inspector)
            if (waveManager == null) waveManager = FindFirstObjectByType<WaveManager>();

            // 2. 🔥 QUAN TRỌNG: KHÔI PHỤC VÍ TIỀN TỪ FILE SAVE
            RestoreEconomyFromSave();

            // 3. ĐĂNG KÝ THEO DÕI WAVE
            if (waveManager != null)
            {
                waveManager.RegisterObserver(this);
            }

            // 4. ĐĂNG KÝ THU NHẬP DÂN THƯỜNG (Phase 8 decoupling: nghe event, không để PopulationSystem gọi trực tiếp)
            TrySubscribeCivilianIncome();

            // 5. CẬP NHẬT GIAO DIỆN LẦN ĐẦU
            UpdateAllUI();
        }

        private bool _incomeSubscribed;

        private void TrySubscribeCivilianIncome()
        {
            if (_incomeSubscribed) return;
            if (PopulationSystem.Instance == null)
            {
                Invoke(nameof(TrySubscribeCivilianIncome), 0.5f);
                return;
            }
            PopulationSystem.Instance.OnCivilianIncomeGenerated += HandleCivilianIncome;
            _incomeSubscribed = true;
        }

        private void HandleCivilianIncome(double income)
        {
            AddGold(income);
        }

        private void OnDestroy()
        {
            if (_incomeSubscribed && PopulationSystem.Instance != null)
            {
                PopulationSystem.Instance.OnCivilianIncomeGenerated -= HandleCivilianIncome;
            }
        }

        /// <summary>
        /// Lấy dữ liệu từ SaveManager để đổ vào các biến hiện tại.
        /// </summary>
        private void RestoreEconomyFromSave()
        {
            if (SaveManager.Instance != null && SaveManager.Instance.currentData != null)
            {
                var data = SaveManager.Instance.currentData;
                currentGold = data.gold;
                whiteGems = data.whiteGems;

                Debug.Log($"<color=cyan>💰 [HỆ THỐNG] Đã lấy lại tài sản: {currentGold} Vàng và {whiteGems} Kim cương.</color>");
            }
            else
            {
                Debug.LogWarning("⚠️ [HỆ THỐNG] Không thấy dữ liệu cũ, dùng mặc định.");
            }
        }

        // ============================================================
        // ⚔️ THỰC THI INTERFACE IWAVEOBSERVER (TRẬN ĐẤU)
        // ============================================================

        public void OnWaveStarted(int waveIndex)
        {
            if (uiManager != null) uiManager.ShowMessage($"Wave {waveIndex} Bắt đầu!");
        }

        public void OnWaveFinished(int waveIndex)
        {
            // 1. Thông báo chiến thắng
            if (uiManager != null)
            {
                uiManager.ShowMessage($"Wave {waveIndex} Hoàn tất!");
                uiManager.UpdateWaveText(waveIndex + 1);
                uiManager.EnableStartButton(true);
            }

            // 2. ✨ HỒI MÁU PHI THUYỀN (FULL HEAL)
            if (CastleController.Instance != null)
            {
                CastleController.Instance.FullHeal();
                Debug.Log("<color=green>✔️ [HỆ THỐNG] Đã hồi đầy máu phi thuyền sau chiến thắng!</color>");
            }

            // 3. THƯỞNG KIM CƯƠNG (Chỉ cộng vào túi trắng, tối đa 80)
            AddGems(1, isFromWave: true);

            UpdateAllUI();
        }

        public void OnEnemyKilled(double goldEarned)
        {
            currentGold += goldEarned;
            UpdateAllUI();
        }

        /// <summary>
        /// Thêm vàng từ nguồn kinh tế khác (vd: thu nhập thụ động dân thường) — dùng chung UI update.
        /// </summary>
        public void AddGold(double amount)
        {
            if (amount <= 0) return;
            currentGold += amount;
            UpdateAllUI();
        }

        public void OnBossKilled(double goldEarned)
        {
            currentGold += goldEarned;
            if (uiManager != null) uiManager.ShowMessage("<color=red>ĐÃ TIÊU DIỆT BOSS!</color>");
            UpdateAllUI();
        }

        // ============================================================
        // 🪙 QUẢN LÝ TÀI CHÍNH (TIỀN TỆ)
        // ============================================================

        public bool SpendGold(double amount)
        {
            if (currentGold >= amount)
            {
                currentGold -= amount;
                UpdateAllUI();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Hệ thống thêm kim cương thông minh.
        /// - isFromWave: Chỉ cộng vào túi trắng (Capped).
        /// - !isFromWave: Ưu tiên lấp đầy túi trắng, dư thì tràn sang túi cam.
        /// </summary>
        public void AddGems(int amount, bool isFromWave)
        {
            if (isFromWave)
            {
                whiteGems = Mathf.Min(whiteGems + amount, maxWhiteGems);
            }
            else
            {
                int spaceLeft = maxWhiteGems - whiteGems;
                if (spaceLeft > 0)
                {
                    int toWhite = Mathf.Min(amount, spaceLeft);
                    whiteGems += toWhite;
                    amount -= toWhite;
                }
                if (amount > 0) orangeGems += amount;
            }
            UpdateAllUI();
        }
        public bool SpendGems(int amount)
        {
            // Kiểm tra xem tổng cả 2 túi có đủ tiền không
            if (whiteGems + orangeGems >= amount)
            {
                if (whiteGems >= amount)
                {
                    // Đủ kim cương trắng, chỉ trừ túi trắng
                    whiteGems -= amount;
                }
                else
                {
                    // Thiếu một ít, trừ hết sạch túi trắng và trừ phần còn lại vào túi cam
                    int remaining = amount - whiteGems;
                    whiteGems = 0;
                    orangeGems -= remaining;
                }

                UpdateAllUI();
                return true;
            }

            return false; // Không đủ tiền chi trả
        }
        private void UpdateAllUI()
        {
            if (uiManager != null)
            {
                uiManager.UpdateGoldText(currentGold);
                // Đồng bộ hiển thị 2 loại kim cương lên thanh UI
                uiManager.UpdateGemDisplay(whiteGems, orangeGems, maxWhiteGems);
            }
        }
    }
}