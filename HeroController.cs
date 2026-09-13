using System.Collections.Generic;
using UnityEngine;

namespace WeGrowTogether.Core
{
    /// <summary>
    /// HeroController: Điều khiển hành vi của lính trên tháp.
    /// Bản nâng cấp: Tối ưu hóa triệt để chống lặp vòng quét mục tiêu khi không có quái.
    /// </summary>
    public class HeroController : MonoBehaviour
    {
        public HeroData data;
        public int level = 1;

        // 🔥 CHỌN LOẠI SÁT THƯƠNG CỦA HERO NÀY (Để truyền cho đạn)
        [Tooltip("Chọn nguyên tố sát thương của Hero này")]
        public ProjectileController.DamageElement heroDamageElement = ProjectileController.DamageElement.Physical;

        private float nextAttackTime;
        private WaveManager waveManager;
        private CharacterAnimation anim;

        // --- HỆ THỐNG BUFF (KHAI BÁO DUY NHẤT TẠI ĐÂY) ---
        private float activeASBuffPercent = 0f;
        private float asBuffTimer = 0f;
        private float vipASBuffPercent = 0f; // Hệ số buff VIP của Wave hiện tại

        // --- HỆ THỐNG QUẢN LÝ SKILL ---
        private List<SkillInstance> skillInstances = new List<SkillInstance>();

        // --- HỆ THỐNG CHỌN MỤC TIÊU (RANDOM TARGET LOCK) ---
        private Transform _currentTarget;

        public void Init(WaveManager manager)
        {
            waveManager = manager;
            anim = GetComponent<CharacterAnimation>();

            if (data != null && data.heroSkills != null)
            {
                skillInstances.Clear();
                foreach (var sData in data.heroSkills)
                {
                    if (sData != null)
                    {
                        skillInstances.Add(new SkillInstance { data = sData, skillLevel = 1 });
                    }
                }
            }

            InvalidateSkillCache();

            if (waveManager != null)
                Debug.Log($"<color=cyan>🏹 {data?.heroName} ĐÃ THÔNG NÒNG, CHỜ QUÁI ĐẾN!</color>");
        }

        void Start()
        {
            if (waveManager == null)
            {
                waveManager = FindFirstObjectByType<WaveManager>();
                if (anim == null) anim = GetComponent<CharacterAnimation>();
            }
        }

        private void OnEnable()
        {
            VipSkillManager.OnVipBuffStateChanged += HandleVipBuffEvent;

            if (VipSkillManager.Instance != null && VipSkillManager.Instance.IsVipBuffActive)
            {
                ApplyVipASBuff(VipSkillManager.Instance.SpeedBuffPercent);
            }
        }

        private void OnDisable()
        {
            VipSkillManager.OnVipBuffStateChanged -= HandleVipBuffEvent;
        }

        void Update()
        {
            if (data == null || waveManager == null) return;
            if (!waveManager.IsWaveActive) return;

            int skillCount = skillInstances.Count;
            for (int i = 0; i < skillCount; i++)
            {
                var skill = skillInstances[i];
                if (skill != null)
                {
                    skill.UpdateCooldown(Time.deltaTime);
                }
            }

            HandleAutoSkills();

            // 🔥 TÍNH TẦM BẮN ĐỘNG TỪ META UPGRADE
            float finalAttackRange = data.baseAttackRange;
            if (MetaUpgradeManager.Instance != null)
            {
                finalAttackRange *= MetaUpgradeManager.Instance.GetMultiplier("Attack Range");
            }

            Debug.DrawLine(transform.position, transform.position + Vector3.right * finalAttackRange, Color.red);

            if (asBuffTimer > 0)
            {
                asBuffTimer -= Time.deltaTime;
                if (asBuffTimer <= 0)
                {
                    activeASBuffPercent = 0f;
                }
            }

            if (Time.time >= nextAttackTime)
            {
                Transform target = AcquireTarget();

                if (target != null)
                {
                    Attack(target);

                    // 🔥 TÍNH TỐC ĐÁNH ĐỘNG TỪ META UPGRADE
                    float currentAS = data.baseAttackSpeed + (level * 0.05f);
                    if (MetaUpgradeManager.Instance != null)
                    {
                        currentAS *= MetaUpgradeManager.Instance.GetMultiplier("Attack Speed");
                    }

                    float totalBuffPercent = activeASBuffPercent + vipASBuffPercent;
                    if (totalBuffPercent > 0)
                    {
                        currentAS += currentAS * (totalBuffPercent / 100f);
                    }

                    float interval = 1f / currentAS;
                    interval = Mathf.Max(0.05f, interval);

                    nextAttackTime = Time.time + interval;
                }
                else
                {
                    nextAttackTime = Time.time + 0.15f;
                }
            }
        }

        private Transform AcquireTarget()
        {
            // 🔥 ÁP DỤNG TẦM BẮN ĐÃ TÍNH TOÁN META UPGRADE
            float range = data.baseAttackRange;
            if (MetaUpgradeManager.Instance != null)
            {
                range *= MetaUpgradeManager.Instance.GetMultiplier("Attack Range");
            }
            float rangeSqr = range * range;

            if (_currentTarget != null && _currentTarget.gameObject.activeInHierarchy)
            {
                float dx = transform.position.x - _currentTarget.position.x;
                float dy = transform.position.y - _currentTarget.position.y;
                if (dx * dx + dy * dy <= rangeSqr)
                {
                    return _currentTarget;
                }
            }

            _currentTarget = waveManager.GetRandomEnemy(transform.position, range);
            return _currentTarget;
        }

        private SkillInstance _cachedActiveSkill;
        private bool _activeSkillCacheDirty = true;

        public SkillInstance GetActiveSkill()
        {
            if (_activeSkillCacheDirty)
            {
                _cachedActiveSkill = skillInstances.Find(s => s != null && s.data != null && s.data.category == SkillCategory.Active);
                _activeSkillCacheDirty = false;
            }
            return _cachedActiveSkill;
        }

        public void InvalidateSkillCache() { _activeSkillCacheDirty = true; }

        public bool TryCastActiveSkill()
        {
            if (waveManager == null || !waveManager.IsWaveActive) return false;

            SkillInstance activeSkill = GetActiveSkill();
            if (activeSkill == null) return false;

            if (activeSkill.currentCooldown > 0) return false;

            if (CastleController.Instance != null && CastleController.Instance.UseMana(activeSkill.data.manaCost))
            {
                if (SkillExecutor.Instance != null)
                {
                    SkillExecutor.Instance.Execute(activeSkill.data, transform, activeSkill.skillLevel);
                    activeSkill.Trigger();
                    return true;
                }
                else
                {
                    Debug.LogError("🚨 LỖI: Không tìm thấy thực thể SkillExecutor trong Scene!");
                }
            }

            return false;
        }

        private void HandleAutoSkills()
        {
            int count = skillInstances.Count;
            for (int i = 0; i < count; i++)
            {
                var skill = skillInstances[i];
                if (skill != null && skill.data != null && skill.data.category == SkillCategory.Auto && skill.currentCooldown <= 0)
                {
                    if (SkillExecutor.Instance != null)
                    {
                        SkillExecutor.Instance.Execute(skill.data, transform, skill.skillLevel);
                        skill.Trigger();
                    }
                }
            }
        }

        public void ApplyAttackSpeedBuff(float percent, float duration)
        {
            activeASBuffPercent = Mathf.Max(activeASBuffPercent, percent);
            asBuffTimer = duration;
            Debug.Log($"<color=red>🔥🔥 {data.heroName} KÍCH HOẠT QUÁ TẢI CƠ KHÍ: +{percent}% Tốc Đánh trong {duration}s!</color>");
        }

        private void HandleVipBuffEvent(bool isActive, float percent)
        {
            if (isActive) ApplyVipASBuff(percent);
            else RemoveVipASBuff();
        }

        private void ApplyVipASBuff(float percent)
        {
            vipASBuffPercent = percent;
            Debug.Log($"<color=gold>⚡ {data.heroName} đã nhận siêu buff VIP: +{percent}% Tốc đánh!</color>");
        }

        private void RemoveVipASBuff()
        {
            vipASBuffPercent = 0f;
        }

        private void Attack(Transform target)
        {
            if (data.projectilePrefab == null)
            {
                Debug.LogWarning($"⚠️ Hero {data.heroName} chưa được gắn đạn trong HeroData!");
                return;
            }
            if (SimpleObjectPooler.Instance == null)
            {
                Debug.LogError("🚨 Không thấy SimpleObjectPooler!");
                return;
            }

            if (anim != null) anim.PlayAttack();

            if (data != null && data.attackSFX != null && SoundManager.Instance != null)
            {
                SoundManager.Instance.PlaySFX(data.attackSFX);
            }

            GameObject projObj = SimpleObjectPooler.Instance.GetFromPool(data.projectilePrefab, transform.position, Quaternion.identity);

            if (projObj != null)
            {
                var projScript = projObj.GetComponent<ProjectileController>();
                if (projScript != null)
                {
                    double currentDmg = GameBalancer.CalculateHeroDamage(data.baseDamage, level);

                    // 🔥 ĐÃ XÓA Physical Dame Ở ĐÂY, VÌ ĐẠN (ProjectileController) SẼ TỰ TÍNH NÓ!

                    // 🔥 ÁP DỤNG TỈ LỆ CHÍ MẠNG TỪ META UPGRADE
                    float finalCritChance = data.criticalChance + (data.criticalChancePerLevel * (level - 1));
                    if (MetaUpgradeManager.Instance != null)
                    {
                        finalCritChance *= MetaUpgradeManager.Instance.GetMultiplier("Critical Chance");
                    }

                    // Random xem có ra chí mạng không
                    bool isCritical = UnityEngine.Random.value < finalCritChance;
                    if (isCritical)
                    {
                        currentDmg *= 2.0; // Sát thương chí mạng nhân 2 (bạn có thể sửa hệ số này)
                        Debug.Log($"<color=yellow>💥 CHÍ MẠNG! {data.heroName} gây {currentDmg} sát thương!</color>");
                    }

                    // Xử lý Skills (Mark, Boss Slayer...)
                    if (data.heroSkills != null)
                    {
                        var monster = target.GetComponent<MonsterController>();
                        var boss = target.GetComponent<BossController>();

                        foreach (var skill in data.heroSkills)
                        {
                            if (skill == null || skill.effects == null) continue;

                            foreach (var effect in skill.effects)
                            {
                                if (!string.IsNullOrEmpty(effect.statusID))
                                {
                                    bool isTargetMarked = (monster != null && monster.HasStatus(effect.statusID)) ||
                                                          (boss != null && boss.HasStatus(effect.statusID));

                                    if (isTargetMarked)
                                    {
                                        currentDmg += currentDmg * (effect.powerValue / 100f);
                                    }
                                }

                                if (effect.bossDamageBonus > 0)
                                {
                                    bool isBossTarget = (boss != null) || (monster != null && monster.isBoss);
                                    if (isBossTarget)
                                    {
                                        currentDmg += currentDmg * (effect.bossDamageBonus / 100f);
                                    }
                                }
                            }
                        }
                    }

                    var rm = ResilienceManager.Instance;
                    if (rm != null)
                    {
                        currentDmg = rm.ApplyResilienceBonus(currentDmg);
                    }

                    // 🔥 TRUYỀN LOẠI SÁT THƯƠNG VÀO ĐẠN
                    projScript.Launch(target, currentDmg, heroDamageElement);
                }
            }
        }

        void OnDrawGizmosSelected()
        {
            if (data != null)
            {
                float range = data.baseAttackRange;
                if (MetaUpgradeManager.Instance != null)
                {
                    range *= MetaUpgradeManager.Instance.GetMultiplier("Attack Range");
                }
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(transform.position, range);
            }
        }

        public void ApplyPetModifier(SynergyStatType type, float val) { }
    }
}