namespace OSE.UI.Root
{
    public static class AnimationCueDefaults
    {
        public static float GetDefaultDuration(string type) => type switch
        {
            "demonstratePlacement" => 1.5f,
            "poseTransition"      => 1.0f,
            "transform"           => 1.5f,
            "pulse"               => 0f,
            "orientSubassembly"   => 0.6f,
            _                     => 1.0f,
        };
    }
}
