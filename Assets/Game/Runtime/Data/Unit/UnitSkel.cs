using System;
using System.Collections.Generic;
using UnityEngine;
using Spine;
using Spine.Unity;

[DisallowMultipleComponent]
public class UnitSkel : MonoBehaviour
{
    [Header("Auto-fetched")]
    [SerializeField] private SkeletonAnimation skel;

    // 和 UnityEngine.AnimationState 区分开
    private Spine.AnimationState state;

    [Header("Animation Info (Auto-filled)")]
    public int animationCount;
    public List<string> animationNames = new List<string>();

    [Header("Move / Speed")]
    [Tooltip("物体自身速度系数：最终动画倍速 = 实际移动速度 / (100 * selfspeed)")]
    public UnitIdentity unitIdentity;
    private float selfspeed = 1f;
    private float selfattackInterval = 1f;

    [Tooltip("动画加速上限，防止异常指令让动画过快")]
    [SerializeField] private float maxAnimTimeScale = 3f;

    [Header("Playback (Base)")]
    [Range(0f, 4f)]
    [SerializeField] private float speed = 1f;           // 基础播放倍速（作用在 SkeletonAnimation.timeScale）
    [SerializeField] private string defaultAnimation = "idle";
    [SerializeField] private bool defaultLoop = true;

    [Header("Move Animation Settings")]
    [Tooltip("Host 指令只由 Move 状态触发，这里指定 Move 动画名")]
    [SerializeField] private string moveAnimationName = "Move";
    [Tooltip("用于播放 Move 的轨道索引")]
    [SerializeField] private int moveTrackIndex = 0;

    private bool warnedOnce;
    private string lastCommandAnim; // 保留：如果后续你要做优化可用

    // 仅用于本次移动周期
    private Coroutine moveRoutine;
    private TrackEntry currentMoveEntry;

    // ---------------- Lifecycle ----------------
    private void Awake()
    {
        if (!skel) skel = GetComponent<SkeletonAnimation>();
        if (!EnsureReady(forceInitialize: true))
        {
            enabled = false;
            return;
        }
        ApplySpeed();
        RefreshAnimationList();

        if (!string.IsNullOrEmpty(defaultAnimation))
        {
            PlayAnimation(defaultAnimation, defaultLoop);
        }
    }
    private void Start()
    {
        LoadSelfData();
    }

    // 注意：按你的建议，已不再使用 Update 推进移动

    // ---------------- Host / 上位机接口（方法一） ----------------
    /// <summary>
    /// 上位机下一条完整指令：
    /// animName: （按你的新约定，本函数会忽略该参数，统一播放 Move 动画）
    /// startPos: 起点
    /// endPos:   终点
    /// duration: 起点走到终点的时间(秒)
    /// 动画倍速（仅作用于 Move 动画）= 实际移动速度 / (100 * selfspeed)
    /// </summary>
    public void ApplyHostMoveCommand(Vector3 startPos, Vector3 endPos, float duration)
    {
        if (!EnsureReady()) return;

        // 1) 强制使用 Move 动画：如果当前不是 Move 就切；如果已经是 Move 则不切
        currentMoveEntry = EnsureMovePlaying();

        // 2) 计算这次位移的“真实移动速度”并设置 Move 的专属 TimeScale（不影响整体动画机）
        // 实际移动速度 = 距离 / 时间
        float dist = Vector3.Distance(startPos, endPos);
        float dur = Mathf.Max(0.0001f, duration);
        float moveSpeed = dist / dur;

        // 先算 100 * 自身速度
        float denom = 100f * Mathf.Max(selfspeed, 0.0001f);  // 防 selfspeed = 0
        // 最终动画播放倍速（仅作用于 Move 条目）
        float entryTimeScale = moveSpeed / denom;
        entryTimeScale = Mathf.Clamp(entryTimeScale, 0.1f, maxAnimTimeScale);

        if (currentMoveEntry != null) currentMoveEntry.TimeScale = entryTimeScale;

        lastCommandAnim = moveAnimationName;

        // 3) 通过协程使用两个关键帧做匀速移动（无需 Update）
        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(MoveRoutine(startPos, endPos, dur));
    }
    public void ApplyHostAtkCommand(int atknumber,float duration)
    {
        try
        {
            if (!EnsureReady()) return;

            // 参数检查
            if (atknumber <= 0 || duration <= 0f)
            {
                Debug.LogWarning("[UnitSkel] ApplyHostAtkCommand: atknumber 和 duration 必须 > 0。", this);
                return;
            }

            // 计算 Attack 播放速率：实际攻击间隔 / 自身攻击间隔
            float actualInterval = duration / atknumber;
            float denom = Mathf.Max(selfattackInterval, 0.0001f); // 防止除 0
            float attackTimeScale = actualInterval / denom;
            attackTimeScale = Mathf.Clamp(attackTimeScale, 0.1f, maxAnimTimeScale);

            // 仅作用于 Attack 动画（固定 0 轨）
            const int trackIndex = 0;
            const string attackAnimName = "Attack";

            // 当前条目
            var entry = state.GetCurrent(trackIndex);
            var currentName = (entry != null && entry.Animation != null) ? entry.Animation.Name : null;

            // 如果当前不是 Attack，就切到 Attack；是的话复用当前条目
            if (!string.Equals(currentName, attackAnimName, StringComparison.Ordinal))
            {
                var data = GetSkeletonData();
                if (data == null || data.FindAnimation(attackAnimName) == null)
                {
                    LogOnce("[UnitSkel] 未找到 Attack 动画：" + attackAnimName);
                    return;
                }
                entry = state.SetAnimation(trackIndex, attackAnimName, true);
            }

            if (entry == null) return;

            // 只加速 Attack 这条 TrackEntry
            entry.TimeScale = attackTimeScale;

            // —— 在本函数内部定义并启动恢复协程（不创建额外方法/成员） ——
            System.Collections.IEnumerator RestoreAfter()
            {
                yield return new WaitForSeconds(duration);

                // 仅当同一个条目仍在同一轨且仍是 Attack 时，才恢复
                var now = state.GetCurrent(trackIndex);
                if (now == entry &&
                    now != null && now.Animation != null &&
                    string.Equals(now.Animation.Name, attackAnimName, StringComparison.Ordinal))
                {
                    now.TimeScale = Mathf.Max(0f, speed); // 恢复到基础倍速
                }
            }
            StartCoroutine(RestoreAfter());
        }
        catch (Exception e)
        {
            Debug.LogError("[UnitSkel] ApplyHostAtkCommand 异常：" + e.Message, this);
        }
    }

    // ---------------- Public API ----------------

    /// <summary>改变动画机整体倍率（基础倍速）。注意：Host Move 指令不会改这个整体倍速。</summary>
    public void SetTotalSpeed(float newSpeed)
    {
        speed = Mathf.Max(0f, newSpeed);
        ApplySpeed();
    }

    public float GetSpeed() => speed;

    public TrackEntry PlayAnimation(string animName, bool loop = true)
    {
        try
        {
            if (!EnsureReady()) return null;

            var data = GetSkeletonData();
            if (data == null)
            {
                LogOnce("[UnitSkel] PlayAnimation 失败：SkeletonData 不可用。");
                return null;
            }

            var anim = data.FindAnimation(animName);
            if (anim == null)
            {
                LogOnce("[UnitSkel] 动画不存在：" + animName);
                return null;
            }

            // 固定 0 轨；不再设置 MixDuration（由 Spine 的默认混合或导入设置决定）
            var entry = state.SetAnimation(0, animName, loop);
            return entry;
        }
        catch (Exception e)
        {
            Debug.LogError("[UnitSkel] PlayAnimation 异常：" + e.Message, this);
            return null;
        }
    }

    public TrackEntry QueueAnimation(string animName, bool loop = true)
    {
        try
        {
            if (!EnsureReady()) return null;

            var data = GetSkeletonData();
            if (data == null)
            {
                LogOnce("[UnitSkel] QueueAnimation 失败：SkeletonData 不可用。");
                return null;
            }

            var anim = data.FindAnimation(animName);
            if (anim == null)
            {
                LogOnce("[UnitSkel] 动画不存在：" + animName);
                return null;
            }

            // 固定 0 轨；固定 delay=0
            var entry = state.AddAnimation(0, animName, loop, 0f);
            return entry;
        }
        catch (Exception e)
        {
            Debug.LogError("[UnitSkel] QueueAnimation 异常：" + e.Message, this);
            return null;
        }
    }

    /// <summary>
    /// 确保 attackTrackIndex 上正在播放 Attack 动画；若不是则切换到 Attack 并循环。
    /// 返回当前 Attack 的 TrackEntry（可用于设置 TimeScale）。
    /// </summary>
    public void RefreshAnimationList() => RefreshAnimationList(false);

    // ---------------- Internals ----------------

    private bool EnsureReady(bool forceInitialize = false)
    {
        if (!skel)
        {
            Debug.LogError("[UnitSkel] 未找到 SkeletonAnimation 组件。", this);
            return false;
        }

        if (!skel.valid && forceInitialize)
        {
            try { skel.Initialize(true); }
            catch (Exception e)
            {
                Debug.LogError("[UnitSkel] Skeleton 初始化失败：" + e.Message, this);
                return false;
            }
        }

        state = skel.AnimationState;
        return skel.valid && state != null;
    }

    private SkeletonData GetSkeletonData()
    {
        if (skel && skel.Skeleton != null && skel.Skeleton.Data != null)
            return skel.Skeleton.Data;

        if (skel && skel.skeletonDataAsset != null)
        {
            try { return skel.skeletonDataAsset.GetSkeletonData(true); }
            catch { return null; }
        }

        return null;
    }

    private void ApplySpeed()
    {
        try
        {
            if (skel) skel.timeScale = Mathf.Max(0f, speed);
        }
        catch (Exception e)
        {
            Debug.LogError("[UnitSkel] 应用倍速失败：" + e.Message, this);
        }
    }

    private void RefreshAnimationList(bool quietIfUnavailable)
    {
        try
        {
            animationNames.Clear();
            var data = GetSkeletonData();

            if (data == null || data.Animations == null)
            {
                animationCount = 0;
                if (!quietIfUnavailable)
                    LogOnce("[UnitSkel] 暂无可用 SkeletonData 或 Animations（资源未初始化/未绑定？）");
                return;
            }

            for (int i = 0; i < data.Animations.Count; i++)
            {
                var anim = data.Animations.Items[i];
                if (anim != null && !string.IsNullOrEmpty(anim.Name))
                    animationNames.Add(anim.Name);
            }

            animationCount = animationNames.Count;
            Debug.Log("[UnitSkel] 动画数量：" + animationCount + "；名称：" + string.Join(", ", animationNames.ToArray()), this);
        }
        catch (Exception e)
        {
            Debug.LogError("[UnitSkel] 刷新动画列表失败：" + e.Message, this);
        }
    }

    /// <summary>
    /// 读取当前轨道上的动画名，没播就返回 null
    /// </summary>
    public string GetCurrentAnimationName(int trackIndex = 0)
    {
        if (!EnsureReady()) return null;
        var current = state.GetCurrent(trackIndex);
        return current != null && current.Animation != null ? current.Animation.Name : null;
    }

    private void LogOnce(string msg)
    {
        if (warnedOnce) return;
        warnedOnce = true;
        Debug.LogWarning(msg, this);
    }

    // --- Helpers for Move-only flow ---

    /// <summary>
    /// 确保 moveTrackIndex 上正在播放 Move 动画；若不是则切换到 Move 并循环。
    /// 返回当前 Move 的 TrackEntry（可用于设置 TimeScale）。
    /// </summary>
    private TrackEntry EnsureMovePlaying()
    {
        if (!EnsureReady()) return null;

        var data = GetSkeletonData();
        if (data == null)
        {
            LogOnce("[UnitSkel] EnsureMovePlaying 失败：SkeletonData 不可用。");
            return null;
        }

        var moveAnim = data.FindAnimation(moveAnimationName);
        if (moveAnim == null)
        {
            LogOnce("[UnitSkel] 未找到 Move 动画：" + moveAnimationName);
            return null;
        }

        var current = state.GetCurrent(moveTrackIndex);
        if (current == null || current.Animation == null || !string.Equals(current.Animation.Name, moveAnimationName, StringComparison.Ordinal))
        {
            current = state.SetAnimation(moveTrackIndex, moveAnimationName, true);
        }
        return current;
    }

    /// <summary>
    /// 通过协程在 duration 内从 startPos 匀速移动到 endPos；完成后将 Move 的 TimeScale 恢复为基础倍速。
    /// </summary>
    private System.Collections.IEnumerator MoveRoutine(Vector3 startPos, Vector3 endPos, float duration)
    {
        transform.position = startPos;

        if (duration <= 0.0001f)
        {
            transform.position = endPos;
            // 恢复 Move 的条目倍速到基础 speed（不影响整体 timeScale 以外的用法）
            if (currentMoveEntry != null) currentMoveEntry.TimeScale = Mathf.Max(0f, speed);
            yield break;
        }

        float startTime = Time.time;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed = Time.time - startTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.position = Vector3.Lerp(startPos, endPos, t);
            yield return null;
        }

        transform.position = endPos;

        // 移动结束：恢复 Move 的条目倍速到基础 speed
        if (currentMoveEntry != null) currentMoveEntry.TimeScale = Mathf.Max(0f, speed);
    }
    private void LoadSelfData()
    {
        selfspeed = UnitFactory.GetUnitBasicValueSO(unitIdentity.UnitTypeID).moveSpeed;
        selfattackInterval = UnitFactory.GetUnitBasicValueSO(unitIdentity.UnitTypeID).attackInterval;
    }
}
