using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnitSkelType1 : UnitSkelBase
{
    public override void ApplyHostMoveCommand(Vector3 startPos, Vector3 endPos, float duration)
    {
        if (!EnsureReady()) return;

        // 三段动画名 & 轨道
        const string BeginName = "Run_Begin";
        const string LoopName = "Run_Loop";
        const string EndName = "Run_End";
        int trackIndex = 0; // 有 moveTrackIndex 就用你的

        // 1. 算这次要用的加速倍速（只对这三个动画生效）
        float dist = Vector3.Distance(startPos, endPos);
        float dur = Mathf.Max(0.0001f, duration);
        float moveSpeed = dist / dur;
        float ts = moveSpeed / (100f * Mathf.Max(selfspeed, 0.0001f));
        ts = Mathf.Clamp(ts, 0.1f, maxAnimTimeScale);

        // 2. 拿动画本体
        var data = GetSkeletonData();
        if (data == null)
        {
            Debug.LogWarning("[UnitSkel] SkeletonData 不可用。", this);
            return;
        }
        var beginAnim = data.FindAnimation(BeginName);
        var loopAnim = data.FindAnimation(LoopName);
        var endAnim = data.FindAnimation(EndName);
        if (beginAnim == null || loopAnim == null || endAnim == null)
        {
            Debug.LogWarning($"[UnitSkel] 缺少三段移动动画，需要 {BeginName}/{LoopName}/{EndName}", this);
            return;
        }

        // 3. 计算这三个动画在“加速后”的真实时长
        float beginPlay = beginAnim.Duration / ts;
        float endPlay = endAnim.Duration / ts;

        // 4. 启动位移（你原来就有的）
        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(MoveRoutine(startPos, endPos, dur));

        // 5. 启动一个本地时间线，精确到点切换动画
        StartCoroutine(MoveTimeline());

        System.Collections.IEnumerator MoveTimeline()
        {
            float startTime = Time.time;

            // 5.1 播放 begin，一开始就加速
            var beginEntry = state.SetAnimation(trackIndex, BeginName, false);
            if (beginEntry != null)
            {
                beginEntry.TimeScale = ts;
                beginEntry.MixDuration = 0f;
            }

            // 终点时刻：end 的最后一帧要在 duration 结束
            // 所以 end 必须在这个时刻开始：
            float endStartTime = Mathf.Max(0f, startTime + dur - endPlay);

            // 5.2 等 begin 播完或者等到必须切 end 的时间，取较早那个
            while (true)
            {
                float now = Time.time;
                // 到时间必须切 end 了
                if (now >= endStartTime)
                    break;

                // begin 正常播完了，可以进 loop
                if (now >= startTime + beginPlay)
                    break;

                yield return null;
            }

            float nowAfterBegin = Time.time;
            // 剩余时间 = 距离“必须开始 end 的时间”还有多少
            float timeLeftForLoop = endStartTime - nowAfterBegin;

            if (timeLeftForLoop <= 0f)
            {
                // 没有中间时间了，直接播 end
                var endEntryNow = state.SetAnimation(trackIndex, EndName, false);
                if (endEntryNow != null)
                {
                    endEntryNow.TimeScale = ts;
                    endEntryNow.MixDuration = 0f;
                }

                // 等 end 播完后恢复
                yield return new WaitForSeconds(endPlay);
                var cur = state.GetCurrent(trackIndex);
                if (cur != null && cur.Animation != null && cur.Animation.Name == EndName)
                    cur.TimeScale = Mathf.Max(0f, speed);
                yield break;
            }

            // 5.3 播放 loop（加速），只播 timeLeftForLoop 这么久，不等它自己播完一整圈
            var loopEntry = state.SetAnimation(trackIndex, LoopName, true);
            if (loopEntry != null)
            {
                loopEntry.TimeScale = ts;
                loopEntry.MixDuration = 0f;
            }

            float loopStart = Time.time;
            while (Time.time - loopStart < timeLeftForLoop)
            {
                // 到了必须开始 end 的时间就掐断
                yield return null;
            }

            // 5.4 到时间了，直接掐断 loop，换 end
            var endEntry = state.SetAnimation(trackIndex, EndName, false);
            if (endEntry != null)
            {
                endEntry.TimeScale = ts;     // end 也按这次的加速走
                endEntry.MixDuration = 0f;
            }

            // 5.5 等 end 正常播完，再恢复到默认倍速
            yield return new WaitForSeconds(endPlay);
            var curAfterEnd = state.GetCurrent(trackIndex);
            if (curAfterEnd != null && curAfterEnd.Animation != null && curAfterEnd.Animation.Name == EndName)
                curAfterEnd.TimeScale = Mathf.Max(0f, speed);
        }
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
