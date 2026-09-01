namespace CarRacing
{
    public static class GlobalSettings
    {
        public const int TotalPlayerCount = 2;
        public const int TickRate = 30;
        public const uint StartDelayTicks = TickRate * 2;
        public const int InputDelayFrames = 2;
        public const int HashIntervalFrames = TickRate;
        public const float PositionUnitsPerUnityUnit = 1_000f;
        public const float HeadingTenthsPerDegree = 10f;
        public const int MaxSteering = 127;
        public const int MaxPedal = 127;
        public const float CameraLookForwardDistance = 8f;// 看向目标前面多远的距离。为正即使目标在屏幕偏下的位置。更多地展现前方的画面。
    }
}
