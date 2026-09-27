using Verse;

namespace AICoopCompanion
{
    public sealed class AICoopPresetPlacement : IExposable
    {
        public string file, source;
        public int mapId, x, z, rotation, room, tick;
        public void ExposeData()
        {
            Scribe_Values.Look(ref file, "file");
            Scribe_Values.Look(ref source, "source");
            Scribe_Values.Look(ref mapId, "mapId");
            Scribe_Values.Look(ref x, "x"); Scribe_Values.Look(ref z, "z");
            Scribe_Values.Look(ref rotation, "rotation");
            Scribe_Values.Look(ref room, "room"); Scribe_Values.Look(ref tick, "tick");
        }
    }
}
