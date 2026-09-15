using Verse;

namespace AICoopCompanion
{
    public class AICoopPresetMarker : Building
    {
        private string markerLabel;

        public string MarkerLabel
        {
            get { return markerLabel; }
            set { markerLabel = value; }
        }

        public override string LabelNoCount
        {
            get { return markerLabel.NullOrEmpty() ? base.LabelNoCount : markerLabel; }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref markerLabel, "markerLabel", string.Empty);
        }
    }
}
