using System;
using System.Linq;
using CMS21_Together_Core.Data.GameType;
using Newtonsoft.Json;

namespace CMS21_Together_Core.Network.Packets
{
    [Flags]
    public enum PartFields
    {
        None = 0,
        Mount = 1,
        Bolts = 2,
        Identity = 4,
        Condition = 8,
        Quality = 16,
        Examined = 32,
        Paint = 64,
        Dust = 128,
        Switched = 256,
        All = 512
    }

    public enum MergeOutcome
    {
        Whole,
        Merged,
        StaleMerged,
        StaleDropped,
        Skipped
    }

    public class Normalised<T> where T : class
    {
        public T Record;
        public PartFields Written;
        public PartFields Stale;
        public MergeOutcome Outcome;

        public bool Dropped => Outcome == MergeOutcome.StaleDropped || Outcome == MergeOutcome.Skipped;
    }

    public static class PartRecordMerge
    {
        private const float Epsilon = 0.001f;

        public const PartFields SubGroups = PartFields.Mount | PartFields.Bolts | PartFields.Identity | PartFields.Condition | PartFields.Quality
                                            | PartFields.Examined | PartFields.Paint | PartFields.Dust;
        public const PartFields BodyGroups = PartFields.Mount | PartFields.Identity | PartFields.Condition | PartFields.Quality | PartFields.Switched;

        public static PartFields Differ(CarSubPartUpdatePacket a, CarSubPartUpdatePacket b)
        {
            var fields = PartFields.None;
            if (a.Unmounted != b.Unmounted) fields |= PartFields.Mount;
            if (!SameBolts(a.MountObjectData, b.MountObjectData)) fields |= PartFields.Bolts;
            if ((a.PartId ?? "") != (b.PartId ?? "") || (a.TunedID ?? "") != (b.TunedID ?? "")) fields |= PartFields.Identity;
            if (!Close(a.Condition, b.Condition)) fields |= PartFields.Condition;
            if (a.Quality != b.Quality) fields |= PartFields.Quality;
            if (a.IsExamined != b.IsExamined) fields |= PartFields.Examined;
            if (a.IsPainted != b.IsPainted || !SameJson(a.Color, b.Color) || a.PaintType != b.PaintType || !SameJson(a.PaintData, b.PaintData)) fields |= PartFields.Paint;
            if (!Close(a.Dust, b.Dust)) fields |= PartFields.Dust;
            return fields;
        }

        public static PartFields Differ(CarBodyPartUpdatePacket a, CarBodyPartUpdatePacket b)
        {
            var fields = PartFields.None;
            if (a.Unmounted != b.Unmounted) fields |= PartFields.Mount;
            if (a.Switched != b.Switched) fields |= PartFields.Switched;
            if ((a.TunedID ?? "") != (b.TunedID ?? "")) fields |= PartFields.Identity;
            if (a.State?.Quality != b.State?.Quality) fields |= PartFields.Quality;
            if (!Close(a.State?.Condition ?? 0f, b.State?.Condition ?? 0f) || !Close(a.State?.Dent ?? 0f, b.State?.Dent ?? 0f)
                || !SameJson(Rest(a.State), Rest(b.State))) fields |= PartFields.Condition;
            return fields;
        }

        public static Normalised<CarSubPartUpdatePacket> Normalise(CarSubPartUpdatePacket stored, CarSubPartUpdatePacket incoming, PartPrecondition precondition)
        {
            var changed = incoming.Changed;
            if (changed == PartFields.None) return new Normalised<CarSubPartUpdatePacket> { Outcome = MergeOutcome.Skipped };
            if (stored == null || changed.HasFlag(PartFields.All) || IsMount(incoming.Unmounted, precondition))
            {
                var whole = Copy(incoming);
                if (stored != null && stored.IsExamined && !whole.IsExamined) whole.IsExamined = true;
                return new Normalised<CarSubPartUpdatePacket> { Record = whole, Written = PartFields.All, Outcome = MergeOutcome.Whole };
            }

            var outcome = MergeOutcome.Merged;
            if (!changed.HasFlag(PartFields.Mount))
            {
                var stale = BaseDiffer(stored, incoming) & ~changed;
                if (stale != PartFields.None) return new Normalised<CarSubPartUpdatePacket> { Stale = stale, Outcome = MergeOutcome.StaleDropped };
                if ((Differ(stored, incoming) & ~changed) != PartFields.None) outcome = MergeOutcome.StaleMerged;
            }

            var merged = WithGroups(stored, incoming, changed);
            if (changed.HasFlag(PartFields.Examined)) merged.IsExamined = incoming.IsExamined || stored.IsExamined;
            return new Normalised<CarSubPartUpdatePacket> { Record = merged, Written = Differ(stored, merged), Outcome = outcome };
        }

        public static Normalised<CarBodyPartUpdatePacket> Normalise(CarBodyPartUpdatePacket stored, CarBodyPartUpdatePacket incoming, PartPrecondition precondition)
        {
            var changed = incoming.Changed;
            if (changed == PartFields.None) return new Normalised<CarBodyPartUpdatePacket> { Outcome = MergeOutcome.Skipped };
            if (stored == null || changed.HasFlag(PartFields.All) || IsMount(incoming.Unmounted, precondition))
                return new Normalised<CarBodyPartUpdatePacket> { Record = Copy(incoming), Written = PartFields.All, Outcome = MergeOutcome.Whole };

            var outcome = MergeOutcome.Merged;
            if (!changed.HasFlag(PartFields.Mount))
            {
                var stale = BaseDiffer(stored, incoming) & ~changed;
                if (stale != PartFields.None) return new Normalised<CarBodyPartUpdatePacket> { Stale = stale, Outcome = MergeOutcome.StaleDropped };
                if ((Differ(stored, incoming) & ~changed) != PartFields.None) outcome = MergeOutcome.StaleMerged;
            }

            var merged = WithGroups(stored, incoming, changed);
            return new Normalised<CarBodyPartUpdatePacket> { Record = merged, Written = Differ(stored, merged), Outcome = outcome };
        }

        public static CarSubPartUpdatePacket WithGroups(CarSubPartUpdatePacket target, CarSubPartUpdatePacket source, PartFields groups)
        {
            if (target == null || groups.HasFlag(PartFields.All)) return Copy(source);
            var merged = Copy(target);
            if (groups.HasFlag(PartFields.Mount)) merged.Unmounted = source.Unmounted;
            if (groups.HasFlag(PartFields.Bolts)) merged.MountObjectData = source.MountObjectData;
            if (groups.HasFlag(PartFields.Identity))
            {
                merged.PartId = source.PartId;
                merged.TunedID = source.TunedID;
            }
            if (groups.HasFlag(PartFields.Condition)) merged.Condition = source.Condition;
            if (groups.HasFlag(PartFields.Quality)) merged.Quality = source.Quality;
            if (groups.HasFlag(PartFields.Examined)) merged.IsExamined = source.IsExamined;
            if (groups.HasFlag(PartFields.Paint))
            {
                merged.IsPainted = source.IsPainted;
                merged.Color = source.Color;
                merged.PaintType = source.PaintType;
                merged.PaintData = source.PaintData;
            }
            if (groups.HasFlag(PartFields.Dust)) merged.Dust = source.Dust;
            return merged;
        }

        public static CarBodyPartUpdatePacket WithGroups(CarBodyPartUpdatePacket target, CarBodyPartUpdatePacket source, PartFields groups)
        {
            if (target == null || groups.HasFlag(PartFields.All)) return Copy(source);
            var merged = Copy(target);
            merged.State = Clone(target.State) ?? new ModItem { ID = target.PartName };
            if (groups.HasFlag(PartFields.Mount)) merged.Unmounted = source.Unmounted;
            if (groups.HasFlag(PartFields.Switched)) merged.Switched = source.Switched;
            if (groups.HasFlag(PartFields.Identity)) merged.TunedID = source.TunedID;
            if (groups.HasFlag(PartFields.Condition) && source.State != null)
            {
                int quality = merged.State.Quality;
                merged.State = Clone(source.State);
                merged.State.Quality = quality;
            }
            if (groups.HasFlag(PartFields.Quality) && source.State != null) merged.State.Quality = source.State.Quality;
            return merged;
        }

        public static PartFields BaseDiffer(CarSubPartUpdatePacket stored, CarSubPartUpdatePacket incoming)
        {
            var fields = PartFields.None;
            if (stored.Unmounted != incoming.Unmounted) fields |= PartFields.Mount;
            if (stored.EffectiveId != incoming.EffectiveId) fields |= PartFields.Identity;
            if (stored.Quality != incoming.Quality) fields |= PartFields.Quality;
            return fields;
        }

        public static PartFields BaseDiffer(CarBodyPartUpdatePacket stored, CarBodyPartUpdatePacket incoming)
        {
            var fields = PartFields.None;
            if (stored.Unmounted != incoming.Unmounted) fields |= PartFields.Mount;
            if (stored.Switched != incoming.Switched) fields |= PartFields.Switched;
            if ((stored.TunedID ?? "") != (incoming.TunedID ?? "")) fields |= PartFields.Identity;
            if (stored.State?.Quality != incoming.State?.Quality) fields |= PartFields.Quality;
            return fields;
        }

        private static bool IsMount(bool unmounted, PartPrecondition precondition) => precondition != null && precondition.WasUnmounted && !unmounted;

        public static CarSubPartUpdatePacket Copy(CarSubPartUpdatePacket record) => new CarSubPartUpdatePacket
        {
            PartIndexPath = record.PartIndexPath, PartId = record.PartId, TunedID = record.TunedID, Unmounted = record.Unmounted,
            Condition = record.Condition, Quality = record.Quality, IsExamined = record.IsExamined, IsPainted = record.IsPainted,
            Color = record.Color, PaintType = record.PaintType, PaintData = record.PaintData, Dust = record.Dust,
            MountObjectData = record.MountObjectData, Revision = record.Revision, Changed = record.Changed,
        };

        public static CarBodyPartUpdatePacket Copy(CarBodyPartUpdatePacket record) => new CarBodyPartUpdatePacket
        {
            PartIndex = record.PartIndex, PartName = record.PartName, Switched = record.Switched, Unmounted = record.Unmounted,
            TunedID = record.TunedID, State = record.State, Revision = record.Revision, Changed = record.Changed,
        };

        public static T WithChanged<T>(T record, PartFields changed) where T : class
        {
            switch (record)
            {
                case CarSubPartUpdatePacket sub:
                    var subCopy = Copy(sub);
                    subCopy.Changed = changed;
                    return subCopy as T;
                case CarBodyPartUpdatePacket body:
                    var bodyCopy = Copy(body);
                    bodyCopy.Changed = changed;
                    return bodyCopy as T;
                default:
                    throw new ArgumentException($"not a part record: {typeof(T).Name}");
            }
        }

        private static ModItem Clone(ModItem item) => item == null ? null : JsonConvert.DeserializeObject<ModItem>(JsonConvert.SerializeObject(item));

        private static ModItem Rest(ModItem item)
        {
            if (item == null) return null;
            var copy = Clone(item);
            copy.Quality = 0;
            copy.Condition = 0f;
            copy.Dent = 0f;
            return copy;
        }

        private static bool Close(float a, float b) => Math.Abs(a - b) < Epsilon;

        private static bool SameJson(object a, object b) => JsonConvert.SerializeObject(a) == JsonConvert.SerializeObject(b);

        private static bool SameBolts(ModMountObjectData a, ModMountObjectData b)
        {
            if (a == null || b == null) return a == null && b == null;
            if ((a.ParentPath ?? "") != (b.ParentPath ?? "")) return false;
            var ca = a.Condition ?? new float[0];
            var cb = b.Condition ?? new float[0];
            var sa = a.IsStuck ?? new bool[0];
            var sb = b.IsStuck ?? new bool[0];
            return ca.Length == cb.Length && sa.Length == sb.Length && !ca.Where((c, i) => !Close(c, cb[i])).Any() && sa.SequenceEqual(sb);
        }
    }
}
