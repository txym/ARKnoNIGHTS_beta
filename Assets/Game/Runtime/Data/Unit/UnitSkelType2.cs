using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnitSkelType2 : UnitSkelBase
{
    public override void ApplyHostMoveCommand(Vector3 startPos, Vector3 endPos, float duration)
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
    public override void ApplyHostAtkCommand(int atknumber, float duration)
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
}
