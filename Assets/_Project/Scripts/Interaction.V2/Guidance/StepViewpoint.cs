using UnityEngine;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Defines a camera viewpoint relative to a step target.
    /// Used by StepGuidanceService and suggested view buttons.
    /// </summary>
    public struct StepViewpoint
    {
        public string Label;
        public float Yaw;
        public float Pitch;
        public float Distance;
        public Vector3 PivotOffset;
    }

    /// <summary>
    /// Standard viewpoint presets for assembly viewing.
    /// Angles are designed for top-of-table assembly scenes.
    /// </summary>
    public static class ViewpointLibrary
    {
        public static StepViewpoint Front => new()
            { Label = "Front", Yaw = 0f, Pitch = 15f, Distance = 1.5f };

        public static StepViewpoint Side => new()
            { Label = "Side", Yaw = 90f, Pitch = 15f, Distance = 1.5f };

        public static StepViewpoint Top => new()
            { Label = "Top", Yaw = 0f, Pitch = 80f, Distance = 2.0f };

        public static StepViewpoint Isometric => new()
            { Label = "Iso", Yaw = 45f, Pitch = 35f, Distance = 1.8f };

        public static StepViewpoint Detail => new()
            { Label = "Detail", Yaw = 30f, Pitch = 25f, Distance = 0.8f };
    }
}
