using System;
using System.Collections.Generic;
using System.Linq;
using YARG.Core.Chart;
using YARG.Core.Engine.Guitar.Engines;
using YARG.Core.Logging;

namespace YARG.Core.Chart.Validation
{
    public static class ChartValidator
    {
        public static int failedValidationMessage = 0;
        // Forbidden two-note combinations for non-pro mode (fret ints)
        private static readonly (int, int)[] ForbiddenTwoNotePairsNonPro =
        {
            (1, 2),
            (3, 4),
            (3, 5),
            (4, 5),
        };

        private static readonly (int, int, int)[] ForbiddenThreeChordPairsOnPro =
        {
            (1, 2, 5),
            (1, 3, 5),
            (1, 4, 5),
        };

        // Public API: validate a SongChart and return user-visible error messages.
        public static List<string> ValidateChart(SongChart chart)
        {
            var errors = new List<string>();

            if (chart == null)
                return errors;

            // Validate the five-fret guitar track if present
            try
            {
                if (chart.FiveFretGuitar != null)
                {
                    errors.AddRange(ValidateFiveFretGuitarTrack(chart.FiveFretGuitar, "FiveFretGuitar"));
                }

                // If you need to validate other tracks (ProGuitar/ProKeys/etc.) add similar calls here.
            }
            catch (Exception ex)
            {
                errors.Add($"Chart validator exception: {ex.Message}");
                YargLogger.LogException(ex, "ChartValidator.ValidateChart threw an exception");
            }

            return errors.Distinct().ToList();
        }

        public static List<string> ValidateFiveFretGuitarTrack(InstrumentTrack<GuitarNote> track, string trackName)
        {
            var errors = new List<string>();
            if (track == null) return errors;

            foreach (Difficulty difficulty in Enum.GetValues(typeof(Difficulty)))
            {
                InstrumentDifficulty<GuitarNote> diff;
                try
                {
                    diff = track.GetDifficulty(difficulty);
                }
                catch
                {
                    // difficulty not present on track
                    continue;
                }

                if (diff?.Notes == null || diff.Notes.Count == 0) continue;

                foreach (var parentNote in diff.Notes)
                {
                    double time = parentNote.Time;
                    // gather all notes in the chord (parent + children)
                    var allNotes = new List<GuitarNote>();
                    foreach (var n in parentNote.AllNotes)
                        allNotes.Add(n);

                    var lanes = allNotes.Select(n => n.Fret).ToArray();

                    if (!YargFiveFretEngine.isProAnchoring)
                    {
                        // Strum / HOPO / Open checks
                        foreach (var n in allNotes)
                        {
                            if (n.IsStrum)
                            {
                                errors.Add(FormatError(trackName, difficulty, time, $"Strum note detected (fret {n.Fret}). Strums are not allowed when Pro Mode is disabled."));
                                failedValidationMessage = 1;
                            }
                            if (n.IsHopo)
                            {
                                errors.Add(FormatError(trackName, difficulty, time, $"HOPO note detected (fret {n.Fret}). HOPOs are not allowed when Pro Mode is disabled."));
                                failedValidationMessage = 2;
                            }
                            if (n.Fret == (int)FiveFretGuitarFret.Open)
                            {
                                errors.Add(FormatError(trackName, difficulty, time, "Open note detected. Open notes are not allowed when Pro Mode is disabled."));
                                failedValidationMessage = 3;
                            }
                        }

                        // Chords >= 3 notes
                        if (allNotes.Count >= 3)
                        {
                            errors.Add(FormatError(trackName, difficulty, time, $"Chord of {allNotes.Count} notes detected (lanes: {string.Join(',', lanes)}). Chords with 3+ notes are not allowed when Pro Mode is disabled."));
                            failedValidationMessage = 4;
                        }

                        // Forbidden two-note pairs
                        if (allNotes.Count == 2)
                        {
                            var a = lanes[0];
                            var b = lanes[1];
                            if (ForbiddenTwoNotePairsNonPro.Any(p => (p.Item1 == a && p.Item2 == b) || (p.Item1 == b && p.Item2 == a)))
                            {
                                errors.Add(FormatError(trackName, difficulty, time, $"Forbidden two-note chord detected: lanes {a} & {b} are not allowed together when Pro Mode is disabled."));
                                failedValidationMessage = 5;
                            }
                        }

                        // On Easy/Medium/Hard: lane 5 (Orange) forbidden
                        if (difficulty == Difficulty.Easy || difficulty == Difficulty.Medium || difficulty == Difficulty.Hard)
                        {
                            if (lanes.Any(l => l == (int)FiveFretGuitarFret.Orange))
                            {
                                errors.Add(FormatError(trackName, difficulty, time, $"Lane 5 (Orange) note detected on {difficulty}. Lane 5 is not allowed on Easy/Medium/Hard when Pro Mode is disabled."));
                                failedValidationMessage = 6;
                            }
                        }
                    }
                    else // isProMode == true
                    {
                        foreach (var n in allNotes)
                        {
                            if (n.IsTap)
                            {
                                errors.Add(FormatError(trackName, difficulty, time, $"Tap note detected (fret {n.Fret}). Taps are not allowed in Pro Mode."));
                                failedValidationMessage = 7;
                            }
                            if (n.Fret == (int)FiveFretGuitarFret.Open)
                            {
                                errors.Add(FormatError(trackName, difficulty, time, "Open note detected. Open notes are not allowed in Pro Mode."));
                                failedValidationMessage = 3;
                            }
                        }

                        // Chords >= 4 notes
                        if (allNotes.Count >= 4)
                        {
                            errors.Add(FormatError(trackName, difficulty, time, $"Chord of {allNotes.Count} notes detected (lanes: {string.Join(',', lanes)}). Chords with 4+ notes are not allowed."));
                            failedValidationMessage = 8;
                        }

                        // Forbidden three-note pairs
                        if (allNotes.Count == 3)
                        {
                            var a = lanes[0];
                            var b = lanes[1];
                            var c = lanes[2];
                            if (ForbiddenThreeChordPairsOnPro.Any(p => (p.Item1 == a && p.Item2 == b && p.Item3 == c) || (p.Item1 == c && p.Item2 == b && p.Item2 == a)))
                            {
                                errors.Add(FormatError(trackName, difficulty, time, $"Forbidden three-note chord detected: lanes {a}, {b}, & {c} are not allowed together on Pro Mode."));
                                failedValidationMessage = 9;
                            }
                        }
                    }
                }
            }

            return errors.Distinct().ToList();
        }

        private static string FormatError(string trackName, Difficulty diff, double timeSeconds, string reason)
        {
            var timestr = TimeSpan.FromSeconds(timeSeconds).ToString(@"mm\:ss\.fff");
            return $"{trackName} [{diff}] @ {timestr}: {reason}";
        }
    }
}