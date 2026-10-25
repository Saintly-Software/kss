using System;
using System.Collections.Generic;
using System.Globalization;
using KirbySuperStar.Movement;

namespace KirbySuperStar.Game;

/// <summary>One line of an input script.</summary>
/// <param name="Verb">Command name: hold, wait, press, shot, preset, teleport, mark or quit.</param>
/// <param name="Input">Buttons held for hold and wait.</param>
/// <param name="Ticks">Ticks to hold the buttons for hold and wait.</param>
/// <param name="Args">Remaining words for the other commands.</param>
public sealed record ScriptCommand(string Verb, KirbyInput Input, int Ticks, string[] Args);

/// <summary>
/// Parses scripted input for automated playback.
/// Each line is one command; <c>#</c> starts a comment.
/// <list type="bullet">
/// <item><c>hold BUTTONS TICKS</c> holds buttons, e.g. <c>hold R J 10</c>. Buttons: L R U D J (jump) A (attack) B (run).</item>
/// <item><c>wait TICKS</c> holds nothing.</item>
/// <item><c>press ACTION</c> presses an input action, such as <c>switch_preset</c>, for the next tick.</item>
/// <item><c>shot FILE</c> saves a screenshot.</item>
/// <item><c>preset classic|modern</c> switches presets.</item>
/// <item><c>teleport X Y</c> moves Kirby, in pixels.</item>
/// <item><c>mark TEXT</c> writes a marker to the trace.</item>
/// <item><c>quit</c> ends the run.</item>
/// </list>
/// </summary>
public static class InputScript
{
    /// <summary>Parses <paramref name="text"/>. Throws <see cref="FormatException"/> on an unknown command or button.</summary>
    public static List<ScriptCommand> Parse(string text)
    {
        var commands = new List<ScriptCommand>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Split('#')[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var verb = words[0].ToLowerInvariant();
            switch (verb)
            {
                case "hold":
                    commands.Add(new ScriptCommand(verb, ParseButtons(words[1..^1]), ParseTicks(words[^1]), Array.Empty<string>()));
                    break;
                case "wait":
                    commands.Add(new ScriptCommand(verb, default, ParseTicks(words[1]), Array.Empty<string>()));
                    break;
                case "press":
                case "shot":
                case "preset":
                case "teleport":
                case "mark":
                case "quit":
                    commands.Add(new ScriptCommand(verb, default, 0, words[1..]));
                    break;
                default:
                    throw new FormatException($"Unknown script command '{words[0]}'.");
            }
        }
        return commands;
    }

    private static int ParseTicks(string word) => int.Parse(word, CultureInfo.InvariantCulture);

    private static KirbyInput ParseButtons(string[] words)
    {
        bool left = false, right = false, up = false, down = false, jump = false, attack = false, run = false;
        foreach (var word in words)
        {
            switch (word.ToUpperInvariant())
            {
                case "L": left = true; break;
                case "R": right = true; break;
                case "U": up = true; break;
                case "D": down = true; break;
                case "J": jump = true; break;
                case "A": attack = true; break;
                case "B": run = true; break;
                default: throw new FormatException($"Unknown button '{word}'.");
            }
        }
        return new KirbyInput(left, right, up, down, jump, attack, run);
    }
}
