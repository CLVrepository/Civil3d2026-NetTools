using System;

namespace CLV_CivilTools.PointCloud
{
    internal readonly record struct CloudGridReviewPosition(int Index, bool PlanView)
    {
        internal CloudGridReviewPosition Move(int delta, int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            return this with { Index = (int)Math.Clamp((long)Index + delta, 0, count - 1) };
        }
        internal CloudGridReviewPosition WithPlan(bool plan) => this with { PlanView = plan };
    }
}
