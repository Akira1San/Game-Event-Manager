using System;

namespace Game
{
    /// <summary>
    /// Music, volume groups, fades and ducking (Task 31).
    ///
    /// This is the whole rule set behind <c>play music</c> / <c>loop music</c> /
    /// <c>fade music</c> / <c>duck music</c> / <c>set music volume</c>, and it is pure: no Flax
    /// types, no engine calls. <see cref="FlaxActions"/> owns the one <c>AudioSource</c> and
    /// asks this class for the volume every tick, so the arithmetic is unit tested without the
    /// editor.
    ///
    /// Volumes are 0–1. There are three levels: <see cref="Master"/> (everything),
    /// <see cref="Music"/> and <see cref="Sound"/>. A fade moves <see cref="Music"/> over time;
    /// a duck multiplies it for a while and then restores it. Starting a new track returns the
    /// music level to what <c>set music volume</c> wrote, fading in when it was left lower —
    /// that is the crossfade the menu → level hand-off needs, with no extra word.
    /// </summary>
    public class AudioMix
    {
        /// <summary>The global a settings UI binds: <c>bind MusicLabel to MusicVolume</c>.</summary>
        public const string MusicVariable = "MusicVolume";
        public const string SoundVariable = "SoundVolume";
        public const string MasterVariable = "MasterVolume";

        /// <summary>How long a freshly started track takes to reach the music level.</summary>
        public const double CrossfadeSeconds = 0.35;
        /// <summary>How long a duck takes to reach its depth.</summary>
        public const double DuckAttackSeconds = 0.25;
        /// <summary>How long a duck takes to come back once its <c>for</c> time runs out.</summary>
        public const double DuckReleaseSeconds = 0.5;

        /// <summary>Every channel's ceiling (`set master volume`).</summary>
        public double Master = 1;
        /// <summary>The music channel's current level (`set music volume`, `fade music`).</summary>
        public double Music = 1;
        /// <summary>The one-shot/step-sound channel (`set sound volume`).</summary>
        public double Sound = 1;
        /// <summary>What <c>set music volume</c> wrote. A new track returns to this level.</summary>
        public double MusicSetting = 1;

        /// <summary>The current track name as written in the event, or empty.</summary>
        public string Track = "";
        /// <summary>True when the current track loops (<c>loop music</c>).</summary>
        public bool TrackLooping;
        /// <summary>True while the bridge should be playing <see cref="Track"/>.</summary>
        public bool Playing;
        /// <summary>Seconds into the track, so a scene change resumes instead of restarting.</summary>
        public double Playhead;

        private bool fading;
        private double fadeFrom;
        private double fadeTo;
        private double fadeDuration;
        private double fadeElapsed;

        private double duckAmount;
        private double duckDuration;
        private double duckElapsed;

        /// <summary>True while a <c>fade music</c> ramp is still moving.</summary>
        public bool Fading => fading;
        /// <summary>Where the running fade will end (the current music level otherwise).</summary>
        public double FadeTarget => fading ? fadeTo : Music;
        /// <summary>True while a <c>duck music</c> is lowering (and then restoring) the music.</summary>
        public bool Ducking => duckAmount > 0;
        public double DuckDepth => duckAmount;

        public static double Clamp01(double value)
        {
            if (double.IsNaN(value))
                return 0;
            return value < 0 ? 0 : value > 1 ? 1 : value;
        }

        public void SetMasterVolume(double value)
        {
            Master = Clamp01(value);
        }

        public void SetSoundVolume(double value)
        {
            Sound = Clamp01(value);
        }

        /// <summary>
        /// <c>set music volume to &lt;n&gt;</c>: the level, set at once. Also the level a new
        /// track fades back to after a fade-out.
        /// </summary>
        public void SetMusicVolume(double value)
        {
            Music = MusicSetting = Clamp01(value);
            fading = false;
        }

        /// <summary>
        /// <c>fade music to &lt;n&gt; in &lt;s&gt;</c>. A fade to 0 is the fade-out idiom and
        /// keeps the music setting, so the next track still comes back at the authored level;
        /// any other target becomes the new setting. A zero duration is an instant set.
        /// </summary>
        public void FadeMusicTo(double target, double seconds)
        {
            target = Clamp01(target);
            if (target > 0)
                MusicSetting = target;
            if (seconds <= 0)
            {
                Music = target;
                fading = false;
                return;
            }
            fadeFrom = Music;
            fadeTo = target;
            fadeDuration = seconds;
            fadeElapsed = 0;
            fading = true;
        }

        /// <summary>
        /// <c>duck music by &lt;n&gt; for &lt;s&gt;</c>: drop the music by n (0.6 = 60%) for s
        /// seconds — a ramp down, a hold and a ramp back, so dialogue never sits on a hard dip.
        /// A second duck replaces the first instead of stacking.
        /// </summary>
        public void DuckMusic(double amount, double seconds)
        {
            amount = Clamp01(amount);
            if (amount <= 0 || seconds <= 0)
                return;
            duckAmount = amount;
            duckDuration = seconds;
            duckElapsed = 0;
        }

        /// <summary>
        /// <c>play music</c> / <c>loop music</c>: the new track owns the music player. The fade
        /// and duck of the previous track are dropped, and a level left below the setting ramps
        /// back up over <see cref="CrossfadeSeconds"/> (the fade-in that follows a fade-out).
        /// </summary>
        public void StartMusic(string name, bool loop)
        {
            Track = string.IsNullOrEmpty(name) ? "" : name.Trim();
            TrackLooping = loop;
            Playing = true;
            Playhead = 0;
            duckAmount = 0;
            duckElapsed = 0;
            if (Music < MusicSetting)
                FadeMusicTo(MusicSetting, CrossfadeSeconds);
        }

        /// <summary>
        /// <c>stop music</c>: playback stops. The music level is left where it is, so a track
        /// started later fades in from that level instead of popping to full.
        /// </summary>
        public void StopMusic()
        {
            Playing = false;
            Track = "";
            Playhead = 0;
            fading = false;
        }

        /// <summary>
        /// The duck's multiplier right now: 1 outside a duck, dipping to <c>1 - amount</c> in the
        /// middle, with a short attack and release inside the authored time.
        /// </summary>
        public double DuckMultiplier
        {
            get
            {
                if (duckAmount <= 0 || duckDuration <= 0)
                    return 1;
                double t = duckElapsed;
                if (t >= duckDuration)
                    return 1;
                double attack = Math.Min(DuckAttackSeconds, duckDuration / 3.0);
                double release = Math.Min(DuckReleaseSeconds, duckDuration / 3.0);
                if (attack > 0 && t < attack)
                    return 1 - duckAmount * (t / attack);
                if (release > 0 && t > duckDuration - release)
                    return 1 - duckAmount * ((duckDuration - t) / release);
                return 1 - duckAmount;
            }
        }

        /// <summary>What the music source's volume should be this tick (0–1).</summary>
        public double EffectiveMusicVolume => Clamp01(Master * Music * DuckMultiplier);
        /// <summary>What every one-shot source's volume should be this tick (0–1).</summary>
        public double EffectiveSoundVolume => Clamp01(Master * Sound);

        /// <summary>Advances the fade, the duck and the playhead. Called once per tick.</summary>
        public void Update(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds))
                seconds = 0;

            if (Playing)
                Playhead += seconds;

            if (fading)
            {
                fadeElapsed += seconds;
                if (fadeElapsed >= fadeDuration)
                {
                    Music = fadeTo;
                    fading = false;
                }
                else
                {
                    Music = fadeFrom + (fadeTo - fadeFrom) * (fadeElapsed / fadeDuration);
                }
            }

            if (duckAmount > 0)
            {
                duckElapsed += seconds;
                if (duckElapsed >= duckDuration)
                {
                    duckAmount = 0;
                    duckElapsed = 0;
                }
            }
        }
    }
}
