using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using AntiqueTradingSimulator.Agents;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AntiqueTradingSimulator.Autoplay
{
    /// <summary>
    /// Applies "-set:target.member=value" overrides, so parameters can be tuned per run without
    /// editing assets or the scene. Three kinds of target:
    ///   - an NPC profile, by Id, ProfileName or asset name:   -set:speculator.TrendSensitivity=0.8
    ///   - a component in the game scene, by class name; private
    ///     [SerializeField] fields work too:                   -set:EconomyManager.longTermDemandCap=0
    ///   - a static field or property, by class name:          -set:Market.OwnTrendWeight=0.3
    /// Profiles are changed on in-memory copies (NpcProfileDatabase.UseRuntimeCopies), never on the
    /// assets. A component field only matters if the game reads it after the scene has loaded
    /// (EconomyManager re-reads its long-term demand fields every week, for example).
    /// </summary>
    public static class AutoplayOverrides
    {
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>One override and what became of it.</summary>
        public sealed class Result
        {
            public string Line;
            public bool Applied;
            public string Message;
        }

        /// <summary>
        /// Applies the overrides aimed at NPC profiles and static members. Run before the game scene
        /// loads, so NPCs are created with the changed profiles. Component overrides are skipped
        /// here and returned unapplied for <see cref="ApplySceneOverrides"/>.
        /// </summary>
        public static List<Result> ApplyEarlyOverrides(IEnumerable<string> lines, out List<string> sceneLines)
        {
            var results = new List<Result>();
            sceneLines = new List<string>();

            var profiles = NpcProfileDatabase.UseRuntimeCopies();
            foreach (string line in lines)
            {
                if (!TrySplit(line, out string target, out string member, out string value))
                {
                    results.Add(Fail(line, "expected target.member=value"));
                    continue;
                }

                var profile = profiles.FirstOrDefault(p => p != null && Matches(p, target));
                if (profile != null)
                {
                    results.Add(Set(line, profile, profile.GetType(), member, value, InstanceMembers));
                    continue;
                }

                var type = FindType(target);
                if (type != null && typeof(Component).IsAssignableFrom(type))
                {
                    sceneLines.Add(line);
                    continue;
                }

                if (type != null)
                {
                    results.Add(Set(line, null, type, member, value, StaticMembers));
                    continue;
                }

                results.Add(Fail(line, $"no NPC profile or class called '{target}'"));
            }

            return results;
        }

        /// <summary>Applies overrides aimed at components of the loaded game scene.</summary>
        public static List<Result> ApplySceneOverrides(IEnumerable<string> lines)
        {
            var results = new List<Result>();
            foreach (string line in lines)
            {
                TrySplit(line, out string target, out string member, out string value);
                var type = FindType(target);
                var component = type != null ? Object.FindFirstObjectByType(type) : null;
                results.Add(component != null
                    ? Set(line, component, type, member, value, InstanceMembers)
                    : Fail(line, $"no {target} in the game scene"));
            }
            return results;
        }

        private static bool Matches(NpcBehaviorProfile profile, string target) =>
            string.Equals(profile.Id, target, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profile.ProfileName, target, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profile.name, target, StringComparison.OrdinalIgnoreCase);

        private static bool TrySplit(string line, out string target, out string member, out string value)
        {
            target = member = value = null;
            int equals = line.IndexOf('=');
            if (equals <= 0) return false;

            string path = line.Substring(0, equals).Trim();
            value = line.Substring(equals + 1).Trim();

            int dot = path.LastIndexOf('.');
            if (dot <= 0 || dot == path.Length - 1) return false;

            target = path.Substring(0, dot);
            member = path.Substring(dot + 1);
            return true;
        }

        /// <summary>A class of this game (not Unity's or .NET's) with the given simple name.</summary>
        private static Type FindType(string name)
        {
            var assembly = typeof(AutoplayOverrides).Assembly;
            return assembly.GetTypes().FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) && !t.IsNested);
        }

        private static Result Set(string line, object instance, Type type, string memberName, string text, BindingFlags flags)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetFields(flags | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(f => string.Equals(f.Name, memberName, StringComparison.OrdinalIgnoreCase));
                if (field != null)
                {
                    if (!TryConvert(text, field.FieldType, out object value))
                        return Fail(line, $"'{text}' is not a valid {field.FieldType.Name}");
                    object before = field.GetValue(instance);
                    field.SetValue(instance, value);
                    return Ok(line, $"{type.Name}.{field.Name}: {Format(before)} -> {Format(value)}");
                }

                var property = current.GetProperties(flags | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(p => string.Equals(p.Name, memberName, StringComparison.OrdinalIgnoreCase) && p.CanWrite);
                if (property != null)
                {
                    if (!TryConvert(text, property.PropertyType, out object value))
                        return Fail(line, $"'{text}' is not a valid {property.PropertyType.Name}");
                    object before = property.CanRead ? property.GetValue(instance) : null;
                    property.SetValue(instance, value);
                    return Ok(line, $"{type.Name}.{property.Name}: {Format(before)} -> {Format(property.CanRead ? property.GetValue(instance) : value)}");
                }
            }

            return Fail(line, $"{type.Name} has no writable member '{memberName}'");
        }

        private static bool TryConvert(string text, Type type, out object value)
        {
            value = null;
            try
            {
                if (type == typeof(string)) { value = text; return true; }
                if (type == typeof(bool))
                {
                    if (text == "1") { value = true; return true; }
                    if (text == "0") { value = false; return true; }
                    value = bool.Parse(text);
                    return true;
                }
                if (type.IsEnum) { value = Enum.Parse(type, text, ignoreCase: true); return true; }
                value = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string Format(object value) =>
            value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "null";

        private static Result Ok(string line, string message)
        {
            Debug.Log($"Autoplay override: {message}");
            return new Result { Line = line, Applied = true, Message = message };
        }

        private static Result Fail(string line, string message)
        {
            Debug.LogError($"Autoplay override '{line}' failed: {message}");
            return new Result { Line = line, Applied = false, Message = message };
        }
    }
}
