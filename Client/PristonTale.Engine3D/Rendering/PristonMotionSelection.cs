using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

public readonly record struct PristonMotionContext(int ItemIndex, uint JobMask, int MapPosition);

public static class PristonMotionSelection
{
    public static PristonMotionInfo Select(PristonModelInfo info, PristonActorMotion state, PristonMotionContext context)
    {
        PristonMotionInfo fallback = null;
        PristonMotionInfo unrestricted = null;
        for (int index = PristonFormatConstants.CharacterMotionBase; index < info.Motions.Count; index++)
        {
            var motion = info.Motions[index];
            if (motion.State != (uint)state || motion.EndFrame <= motion.StartFrame)
            {
                continue;
            }

            if ((motion.MapPosition != 0 && context.MapPosition != 0 && (motion.MapPosition & context.MapPosition) == 0)
                || (motion.JobCodeBit != 0 && context.JobMask != 0 && (motion.JobCodeBit & context.JobMask) == 0))
            {
                continue;
            }

            fallback ??= motion;
            if (motion.ItemCodeCount == 0)
            {
                unrestricted ??= motion;
                continue;
            }

            var item = context.MapPosition == 1 ? -1 : context.ItemIndex;
            foreach (var allowed in motion.ItemIndices)
            {
                if ((item < 0 && allowed is -1 or byte.MaxValue) || item == allowed)
                {
                    return motion;
                }
            }
        }

        return unrestricted ?? fallback;
    }
}
