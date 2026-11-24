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

        /// <summary>Reload length used when the acting sheet has no resolvable weapon. The reload
        /// itself is an ordinary named timer in <see cref="Cooldowns"/> (Task 37 F5).</summary>
        public const double DefaultReload = 1.5;

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
        private static readonly DataTable Table = new DataTable("WeaponData", FieldNames);

        /// <summary>
        /// Reads <c>&lt;Weapon&gt;&lt;Field&gt;</c> assignments out of a data file, e.g.
        /// <c>when game starts</c> → <c>set PistolReload to 0.6</c>. Called once per load/reload
        /// by the bridge; pass null to forget the previous table.
        /// </summary>
        public static void LoadData(ProgramFile file)
        {
            Table.LoadData(file);
        }

        /// <summary>The names of the built-in weapons, sorted, for warning messages.</summary>
        public static string KnownNames()
        {
            // Built-in defaults plus whatever WeaponData.events defines, so a data-only
            // weapon (the Flamer the Flamer enemy carries) is named in the help too.
            var names = Table.MergeWith(Builtins.Keys);
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
            return Table.CanonicalFor(variables, weaponName);
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
                if (Table.TryRead(variables, canonical, field, out ValueBox value))
                    Apply(result, field, value);
            stats = result;
            return true;
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

        /// <summary>
        /// The weapon the acting sheet currently holds, or null when none is set. The numbers are
        /// the shooter's own (Task 27 Part B): one player's upgrade never buffs the other's guns.
        /// </summary>
        public static WeaponStats ForActor(EventContext context)
        {
            if (context == null)
                return null;
            string name = context.ReadMy(CurrentWeaponVariable).AsText();
            if (string.IsNullOrEmpty(name))
                return null;
            if (!TryGet(context.Variables, name, out WeaponStats stats))
                return null;
            return ApplyPlayerScope(context.Variables, Tech.PlayerIndexOf(context), stats);
        }

        /// <summary>
        /// Fields the per-player formula (× multiplier + bonus) applies to. Everything else is a
        /// name or a flag, so a player value replaces it outright instead.
        /// </summary>
        private static readonly HashSet<string> NumericFields =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Damage", "FireRate", "Spread", "Multiplier", "Reload", "AmmoPerShot", "Ammo",
                "Speed", "Knockback", "Life", "Blast",
            };

        /// <summary>
        /// One weapon field with a player's tech on top (Task 27 Part B): <c>Player2 PistolDamage</c>
        /// is a bonus, <c>Player2 PistolDamageBonusMultiplier</c> a multiplier, and the result is
        /// <c>base × multiplier + bonus</c>. Text and flag fields take a plain override
        /// (<c>Player2 PistolBullet</c>). The base is whatever <see cref="TryGet"/> resolves, so
        /// WeaponData.events keeps owning the shared numbers.
        /// </summary>
        public static ValueBox Effective(EventVariables variables, int playerIndex, string weaponName, string field)
        {
            if (field == null || !TryGet(variables, weaponName, out WeaponStats stats))
                return new ValueBox();
            return EffectiveField(variables, playerIndex, stats, field);
        }

        private static ValueBox EffectiveField(EventVariables variables, int playerIndex,
            WeaponStats stats, string field)
        {
            ValueBox baseValue = Current(stats, field);
            if (NumericFields.Contains(field))
                return ValueBox.OfNumber(Tech.Scale(variables, playerIndex, stats.Name + field,
                    baseValue.AsNumber()));
            if (Tech.TryOverride(variables, playerIndex, stats.Name + field, out ValueBox playerValue))
                return playerValue;
            return baseValue;
        }

        /// <summary>Copies the player-scoped values over a resolved weapon's shared numbers.</summary>
        private static WeaponStats ApplyPlayerScope(EventVariables variables, int playerIndex, WeaponStats stats)
        {
            if (variables == null || playerIndex < 0)
                return stats;
            foreach (string field in FieldNames)
                Apply(stats, field, EffectiveField(variables, playerIndex, stats, field));
            return stats;
        }

        /// <summary>Reads one field back out of resolved stats, for the per-player formula.</summary>
        private static ValueBox Current(WeaponStats stats, string field)
        {
            switch (field)
            {
                case "Bullet": return ValueBox.OfText(stats.Bullet);
                case "Damage": return ValueBox.OfNumber(stats.Damage);
                case "FireRate": return ValueBox.OfNumber(stats.FireRate);
                case "Spread": return ValueBox.OfNumber(stats.Spread);
                case "Multiplier": return ValueBox.OfNumber(stats.Multiplier);
                case "Reload": return ValueBox.OfNumber(stats.Reload);
                case "Sound": return ValueBox.OfText(stats.Sound);
                case "Smoke": return ValueBox.OfText(stats.Smoke);
                case "AmmoPerShot": return ValueBox.OfNumber(stats.AmmoPerShot);
                case "Ammo": return ValueBox.OfNumber(stats.Ammo);
                case "Speed": return ValueBox.OfNumber(stats.Speed);
                case "Knockback": return ValueBox.OfNumber(stats.Knockback);
                case "Life": return ValueBox.OfNumber(stats.Life);
                case "Blast": return ValueBox.OfNumber(stats.Blast);
                case "Bounce": return ValueBox.OfBoolean(stats.Bounce);
                case "Explosive": return ValueBox.OfBoolean(stats.Explosive);
                case "Pierce": return ValueBox.OfBoolean(stats.Pierce);
                default: return new ValueBox();
            }
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
}
