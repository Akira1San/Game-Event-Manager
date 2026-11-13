using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>One weapon's data (Task 16 Part C): what a shot costs and how it comes out.</summary>
    public class WeaponStats
    {
        public string Name = "";
        /// <summary>The actor template `spawn Bullet` clones while this weapon is held.</summary>
        public string Bullet = "Bullet";
        public double Damage = 10;
        /// <summary>Seconds between shots (Part C.5 enforces it; the sheet may read it too).</summary>
        public double FireRate = 0.25;
        public double Spread;
        public double Multiplier = 1;
        /// <summary>Seconds a `reload` takes.</summary>
        public double Reload = 1.5;
        public string Sound = "";
        public string Smoke = "";
        public double AmmoPerShot = 1;
        /// <summary>Ammo a fresh actor starts with when nothing has set the count yet.</summary>
        public double Ammo = 12;
        // ---- Part C.5: how the projectile behaves (copied onto each bullet at spawn) ----
        /// <summary>Projectile speed in cm/s.</summary>
        public double Speed = 400;
        /// <summary>Knockback strength applied to a victim (`push other back strength &lt;n&gt;`).</summary>
        public double Knockback = 100;
        /// <summary>Projectile lifetime in seconds (0 = the sheet's own default).</summary>
        public double Life = 3;
        /// <summary>Blast radius in cm for an <c>Explosive</c> projectile.</summary>
        public double Blast;
        /// <summary>The projectile reflects off walls instead of dying.</summary>
        public bool Bounce;
        /// <summary>The projectile explodes where it stops (wall or victim).</summary>
        public bool Explosive;
        /// <summary>The projectile passes through a victim instead of stopping at it.</summary>
        public bool Pierce;
    }

    /// <summary>
    /// Weapon definitions (Task 16 Part C), shaped exactly like <see cref="EnemyTypes"/>.
    ///
    /// A weapon is a bundle of numbers and names. <c>set weapon to "Pistol"</c> records the
    /// actor's current weapon in <c>my Weapon</c>; the ammo count lives in that actor's own
    /// <c>&lt;Weapon&gt;Ammo</c> variable (so two players can hold the same pistol with different
    /// counts), and <c>spawn Bullet</c> clones the current weapon's bullet type.
    ///
    /// The stats stay data: a global variable named <c>&lt;Weapon&gt;&lt;Field&gt;</c> — for
    /// example <c>PistolReload</c>, set in <c>WeaponData.events</c> — overrides the built-in TK4
    /// values, and a weapon that exists only as such variables is a valid weapon.
    /// </summary>
    public static class WeaponData
    {
        /// <summary>The actor variable holding the current weapon's name (<c>my Weapon</c>).</summary>
        public const string CurrentWeaponVariable = "Weapon";

        /// <summary>Every field a weapon has, in a stable order.</summary>
        public static readonly string[] FieldNames =
        {
            "Bullet", "Damage", "FireRate", "Spread", "Multiplier", "Reload", "Sound", "Smoke",
            "AmmoPerShot", "Ammo",
            // Part C.5 — projectile behaviour (see BulletMath / TK_Bullet.events).
            "Speed", "Knockback", "Life", "Blast", "Bounce", "Explosive", "Pierce",
        };

        private static readonly Dictionary<string, WeaponStats> Builtins =
            new Dictionary<string, WeaponStats>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "Pistol", new WeaponStats
                    {
                        Name = "Pistol", Bullet = "Bullet",
                        Damage = 10, FireRate = 0.25, Spread = 0, Multiplier = 1,
                        Reload = 1.2, Sound = "gunshot", Smoke = "muzzle",
                        AmmoPerShot = 1, Ammo = 25,
                        Speed = 400, Knockback = 100, Life = 3, Blast = 0,
                    }
                },
                {
                    "Shotgun", new WeaponStats
                    {
                        Name = "Shotgun", Bullet = "ShotgunPellet",
                        Damage = 6, FireRate = 0.8, Spread = 12, Multiplier = 6,
                        Reload = 2, Sound = "shotgun", Smoke = "muzzle",
                        AmmoPerShot = 1, Ammo = 8,
                        // Six pellets, a wide cone, and short legs: a shotgun is a close-range gun.
                        Speed = 360, Knockback = 80, Life = 0.8, Blast = 0,
                    }
                },
                {
                    "Uzi", new WeaponStats
                    {
                        Name = "Uzi", Bullet = "UziBullet",
                        Damage = 5, FireRate = 0.08, Spread = 4, Multiplier = 1,
                        Reload = 1, Sound = "uzi", Smoke = "muzzle",
                        AmmoPerShot = 1, Ammo = 40,
                        Speed = 460, Knockback = 40, Life = 2.2, Blast = 0,
                    }
                },
                {
                    "Rocket", new WeaponStats
                    {
                        Name = "Rocket", Bullet = "Rocket",
                        Damage = 60, FireRate = 1.2, Spread = 0, Multiplier = 1,
                        Reload = 2.5, Sound = "rocket", Smoke = "smoke",
                        AmmoPerShot = 1, Ammo = 4,
                        // Slow, heavy and explosive: walls and victims both get a blast.
                        Speed = 180, Knockback = 200, Life = 6, Blast = 320,
                        Explosive = true,
                    }
                },
                {
                    "Grenade", new WeaponStats
                    {
                        Name = "Grenade", Bullet = "Grenade",
                        Damage = 50, FireRate = 1.5, Spread = 0, Multiplier = 1,
                        Reload = 2.5, Sound = "throw", Smoke = "smoke",
                        AmmoPerShot = 1, Ammo = 3,
                        Speed = 120, Knockback = 250, Life = 6, Blast = 400,
                        Explosive = true,
                    }
                },
            };

        /// <summary>
        /// Stats authored in a data file (`WeaponData.events`), keyed by weapon then field. Read
        /// at load time so a weapon's numbers exist before any actor's `me appears` runs.
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, ValueBox>> Authored =
            new Dictionary<string, Dictionary<string, ValueBox>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Reads <c>&lt;Weapon&gt;&lt;Field&gt;</c> assignments out of a data file, e.g.
        /// <c>when game starts</c> → <c>set PistolReload to 0.6</c>. Called once per load/reload
        /// by the bridge; pass null to forget the previous table.
        /// </summary>
        public static void LoadData(ProgramFile file)
        {
            Authored.Clear();
            if (file == null)
                return;
            foreach (EventNode ev in file.Events)
            {
                foreach (BodyLineNode line in ev.Body)
                {
                    if (!(line.Statement is SetNode set) || set.Op != '\0')
                        continue;
                    if (!TrySplitField(set.Target.Name, out string weapon, out string field))
                        continue;
                    if (!TryLiteral(set.Value, out ValueBox value))
                        continue;
                    if (!Authored.TryGetValue(weapon, out var stats))
                    {
                        stats = new Dictionary<string, ValueBox>(StringComparer.OrdinalIgnoreCase);
                        Authored[weapon] = stats;
                    }
                    stats[field] = value;
                }
            }
        }

        /// <summary>Splits `PistolReload` into the weapon `Pistol` and the field `Reload`.</summary>
        private static bool TrySplitField(string name, out string weapon, out string field)
        {
            weapon = "";
            field = "";
            if (string.IsNullOrEmpty(name))
                return false;
            foreach (string candidate in FieldNames)
            {
                if (name.Length <= candidate.Length ||
                    !name.EndsWith(candidate, StringComparison.OrdinalIgnoreCase))
                    continue;
                weapon = name.Substring(0, name.Length - candidate.Length);
                field = candidate;
                return true;
            }
            return false;
        }

        private static bool TryLiteral(ValueNode node, out ValueBox value)
        {
            switch (node)
            {
                case NumberValue number:
                    value = ValueBox.OfNumber(number.Value);
                    return true;
                case TextValue text:
                    value = ValueBox.OfText(text.Text);
                    return true;
                case BooleanValue boolean:
                    value = ValueBox.OfBoolean(boolean.Value);
                    return true;
                default:
                    value = new ValueBox();
                    return false;
            }
        }

        /// <summary>The names of the built-in weapons, sorted, for warning messages.</summary>
        public static string KnownNames()
        {
            var names = new List<string>(Builtins.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(", ", names);
        }

        /// <summary>
        /// The spelling to store in <c>my Weapon</c>, or null when nothing defines that weapon.
        /// A built-in name wins (any casing); otherwise the weapon must have at least one
        /// <c>&lt;Weapon&gt;&lt;Field&gt;</c> global.
        /// </summary>
        public static string CanonicalFor(EventVariables variables, string weaponName)
        {
            if (string.IsNullOrEmpty(weaponName))
                return null;
            foreach (var pair in Builtins)
                if (string.Equals(pair.Key, weaponName, StringComparison.OrdinalIgnoreCase))
                    return pair.Key;
            foreach (var pair in Authored)
                if (string.Equals(pair.Key, weaponName, StringComparison.OrdinalIgnoreCase))
                    return pair.Key;
            foreach (string field in FieldNames)
                if (variables != null && variables.HasGlobal(weaponName + field))
                    return weaponName;
            return null;
        }

        /// <summary>Resolves a weapon's full data (built-in defaults, then the data file, then globals).</summary>
        public static bool TryGet(EventVariables variables, string weaponName, out WeaponStats stats)
        {
            stats = null;
            string canonical = CanonicalFor(variables, weaponName);
            if (canonical == null)
                return false;
            var result = new WeaponStats();
            if (Builtins.TryGetValue(canonical, out WeaponStats builtin))
            {
                result = new WeaponStats
                {
                    Name = builtin.Name,
                    Bullet = builtin.Bullet,
                    Damage = builtin.Damage,
                    FireRate = builtin.FireRate,
                    Spread = builtin.Spread,
                    Multiplier = builtin.Multiplier,
                    Reload = builtin.Reload,
                    Sound = builtin.Sound,
                    Smoke = builtin.Smoke,
                    AmmoPerShot = builtin.AmmoPerShot,
                    Ammo = builtin.Ammo,
                    Speed = builtin.Speed,
                    Knockback = builtin.Knockback,
                    Life = builtin.Life,
                    Blast = builtin.Blast,
                    Bounce = builtin.Bounce,
                    Explosive = builtin.Explosive,
                    Pierce = builtin.Pierce,
                };
            }
            result.Name = canonical;
            foreach (string field in FieldNames)
                if (TryField(variables, canonical, field, out ValueBox value))
                    Apply(result, field, value);
            stats = result;
            return true;
        }

        /// <summary>One field: the data file's global when it exists, otherwise the built-in value.</summary>
        private static bool TryField(EventVariables variables, string weaponName, string field, out ValueBox value)
        {
            string authored = weaponName + field;
            if (variables != null && variables.HasGlobal(authored))
            {
                value = ValueBox.Copy(variables.GetGlobal(authored));
                return true;
            }
            if (Authored.TryGetValue(weaponName, out var authoredStats) &&
                authoredStats.TryGetValue(field, out ValueBox fromData))
            {
                value = ValueBox.Copy(fromData);
                return true;
            }
            value = new ValueBox();
            return false;
        }

        private static void Apply(WeaponStats stats, string field, ValueBox value)
        {
            switch (field)
            {
                case "Bullet": stats.Bullet = value.AsText(); break;
                case "Damage": stats.Damage = value.AsNumber(); break;
                case "FireRate": stats.FireRate = value.AsNumber(); break;
                case "Spread": stats.Spread = value.AsNumber(); break;
                case "Multiplier": stats.Multiplier = value.AsNumber(); break;
                case "Reload": stats.Reload = value.AsNumber(); break;
                case "Sound": stats.Sound = value.AsText(); break;
                case "Smoke": stats.Smoke = value.AsText(); break;
                case "AmmoPerShot": stats.AmmoPerShot = value.AsNumber(); break;
                case "Ammo": stats.Ammo = value.AsNumber(); break;
                case "Speed": stats.Speed = value.AsNumber(); break;
                case "Knockback": stats.Knockback = value.AsNumber(); break;
                case "Life": stats.Life = value.AsNumber(); break;
                case "Blast": stats.Blast = value.AsNumber(); break;
                case "Bounce": stats.Bounce = value.AsBoolean(); break;
                case "Explosive": stats.Explosive = value.AsBoolean(); break;
                case "Pierce": stats.Pierce = value.AsBoolean(); break;
            }
        }

        /// <summary>The weapon the acting sheet currently holds, or null when none is set.</summary>
        public static WeaponStats ForActor(EventContext context)
        {
            if (context == null)
                return null;
            string name = context.ReadMy(CurrentWeaponVariable).AsText();
            if (string.IsNullOrEmpty(name))
                return null;
            return TryGet(context.Variables, name, out WeaponStats stats) ? stats : null;
        }

        /// <summary>The actor variable holding one weapon's ammo count (<c>PistolAmmo</c>).</summary>
        public static string AmmoVariable(string weaponName)
        {
            return (weaponName ?? "") + "Ammo";
        }

        /// <summary>
        /// The actor's ammo for a weapon: its own count when one was written, otherwise the
        /// weapon's starting ammo from the data. Never negative.
        /// </summary>
        public static double ReadAmmo(EventContext context, string weaponName)
        {
            if (context == null || string.IsNullOrEmpty(weaponName))
                return 0;
            string variable = AmmoVariable(weaponName);
            if (context.Variables.HasLocal(context.Me, variable))
            {
                double held = context.Variables.GetLocal(context.Me, variable).AsNumber();
                return held < 0 ? 0 : held;
            }
            return TryGet(context.Variables, weaponName, out WeaponStats stats) ? stats.Ammo : 0;
        }

        /// <summary>Writes one weapon's ammo count on the acting actor, clamped at 0.</summary>
        public static void WriteAmmo(EventContext context, string weaponName, double amount)
        {
            if (context == null || string.IsNullOrEmpty(weaponName))
                return;
            context.WriteMy(AmmoVariable(weaponName), ValueBox.OfNumber(amount < 0 ? 0 : amount));
        }
    }

    /// <summary>
    /// The geometry behind weapon variants (Task 16 Part C.5): how a spray of pellets fans out,
    /// and how a projectile leaves a wall it hit.
    ///
    /// Kept here, free of engine types, so both are unit-testable — the bridge only turns these
    /// angles into orientations and the ray's normal into an argument.
    /// </summary>
    public static class BulletMath
    {
        /// <summary>
        /// The yaw offsets (degrees) for one shot: <paramref name="count"/> pellets spread evenly
        /// across <paramref name="spreadDegrees"/>, centred on the aim. One pellet (or no spread)
        /// keeps the exact heading, so a pistol is as accurate as it looks.
        /// </summary>
        public static double[] SpreadAngles(double spreadDegrees, int count)
        {
            if (count < 1)
                count = 1;
            var angles = new double[count];
            if (count == 1 || spreadDegrees <= 0)
                return angles;
            double step = spreadDegrees / (count - 1);
            double start = -spreadDegrees / 2;
            for (int i = 0; i < count; i++)
                angles[i] = start + step * i;
            return angles;
        }

        /// <summary>
        /// Reflects a flat (XZ) travel direction off a surface normal. Both are unnormalized on
        /// input; the result is a unit vector. A head-on hit (normal opposite the travel)
        /// reverses the direction, and a 45° graze leaves at the mirrored angle.
        /// </summary>
        public static void Reflect(double dirX, double dirZ, double normalX, double normalZ,
            out double outX, out double outZ)
        {
            double length = Math.Sqrt(dirX * dirX + dirZ * dirZ);
            if (length < 1e-6)
            {
                outX = 0;
                outZ = 1;
                return;
            }
            double dx = dirX / length;
            double dz = dirZ / length;
            double nLength = Math.Sqrt(normalX * normalX + normalZ * normalZ);
            if (nLength < 1e-6)
            {
                outX = -dx;
                outZ = -dz;
                return;
            }
            double nx = normalX / nLength;
            double nz = normalZ / nLength;
            double dot = dx * nx + dz * nz;
            double rx = dx - 2 * dot * nx;
            double rz = dz - 2 * dot * nz;
            // A degenerate reflection (numerical noise straight into the wall) flips instead.
            if (Math.Abs(rx) < 1e-6 && Math.Abs(rz) < 1e-6)
            {
                rx = -dx;
                rz = -dz;
            }
            double outLength = Math.Sqrt(rx * rx + rz * rz);
            if (outLength < 1e-6)
            {
                outX = -dx;
                outZ = -dz;
                return;
            }
            outX = rx / outLength;
            outZ = rz / outLength;
        }

        /// <summary>True when a flat offset is inside a blast radius (negative radius = no blast).</summary>
        public static bool WithinBlast(double offsetX, double offsetZ, double radius)
        {
            if (radius <= 0)
                return false;
            return offsetX * offsetX + offsetZ * offsetZ <= radius * radius;
        }
    }

    /// <summary>
    /// Per-actor reload timers (Task 16 Part C). <c>reload</c> starts one for the current
    /// weapon's reload time; <c>reloading</c> is true until it runs out, which is what suspends
    /// shooting (<c>unless reloading</c>), and <c>when reload completes</c> fires once when it does.
    ///
    /// Kept free of engine types so the timing is unit-testable: the caller supplies "now".
    /// </summary>
    public class WeaponClock
    {
        /// <summary>Reload length used when the acting sheet has no resolvable weapon.</summary>
        public const double DefaultReload = 1.5;

        private readonly Dictionary<object, double> ends = new Dictionary<object, double>();
        private readonly Dictionary<object, int> completions = new Dictionary<object, int>();

        /// <summary><c>reload</c>: begin (or restart) a reload that ends <paramref name="seconds"/> from now.</summary>
        public void Start(object actor, double seconds, double now)
        {
            if (actor == null)
                return;
            if (seconds < 0)
                seconds = 0;
            ends[actor] = now + seconds;
        }

        /// <summary><c>reloading</c>: true while the timer still has time on it.</summary>
        public bool IsReloading(object actor, double now)
        {
            return actor != null && ends.TryGetValue(actor, out double end) && now < end;
        }

        /// <summary>Seconds left on the reload, or 0 when the actor is not reloading.</summary>
        public double Remaining(object actor, double now)
        {
            if (actor == null || !ends.TryGetValue(actor, out double end) || now >= end)
                return 0;
            return end - now;
        }

        /// <summary>
        /// How many reloads the actor has finished, counting one the first time it is asked
        /// after the timer runs out. Comparing that count against the one a sheet saw last
        /// makes <c>when reload completes</c> a one-shot edge, however many sheets are polling.
        /// </summary>
        public int Completion(object actor, double now)
        {
            if (actor == null)
                return 0;
            completions.TryGetValue(actor, out int count);
            if (ends.TryGetValue(actor, out double end) && now >= end)
            {
                ends.Remove(actor);
                count++;
                completions[actor] = count;
            }
            return count;
        }

        /// <summary>Switching weapons drops any reload in progress (the new one starts ready).</summary>
        public void Cancel(object actor)
        {
            if (actor == null)
                return;
            ends.Remove(actor);
        }

        /// <summary>Forget an actor entirely (it was destroyed).</summary>
        public void Clear(object actor)
        {
            if (actor == null)
                return;
            ends.Remove(actor);
            completions.Remove(actor);
        }

        /// <summary>Actors with a reload still running.</summary>
        public List<object> ActiveActors()
        {
            return new List<object>(ends.Keys);
        }
    }
}
