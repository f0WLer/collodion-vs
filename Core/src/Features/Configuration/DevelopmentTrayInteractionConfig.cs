namespace Photocore.Configuration
{
    public sealed class DevelopmentTrayInteractionConfig
    {
        public TimedInteractionConfig Developer = new TimedInteractionConfig
        {
            DurationSeconds = 1.25f
        };

        public TimedInteractionConfig Fixer = new TimedInteractionConfig
        {
            DurationSeconds = 1.25f
        };

        public TimedInteractionConfig Water = new TimedInteractionConfig
        {
            DurationSeconds = 1.25f
        };

        internal void ClampInPlace()
        {
            Developer ??= new TimedInteractionConfig { DurationSeconds = 1.25f };
            Fixer ??= new TimedInteractionConfig { DurationSeconds = 1.25f };
            Water ??= new TimedInteractionConfig { DurationSeconds = 1.25f };
            Developer.ClampInPlace();
            Fixer.ClampInPlace();
            Water.ClampInPlace();
        }
    }
}
