namespace ShooterLoop;

using System;
using System.Collections.Generic;

/// <summary>What happened when a code was submitted, so the UI can say which.</summary>
public enum CodeRedeemResult { Ok, Unknown, AlreadyUsed }

public readonly struct SecretCode
{
    public string Code { get; init; }

    /// <summary>Shown after a successful redeem — the player has to be told what they just got.</summary>
    public string Reward { get; init; }

    /// <summary>Applied once, then recorded so it can't be redeemed again.</summary>
    public Action<GameManager> Grant { get; init; }
}

// Codes are matched case- and space-insensitively (see Normalise) because they get typed on a phone
// keyboard, where an autocapitalised first letter or a trailing space is the norm rather than a typo
// worth punishing.
//
// Every grant here goes through an existing GameManager mutator rather than writing state directly,
// so a code can't put the save file into a shape the rest of the game couldn't have produced. That's
// also why there's no "unlock everything" code: nothing in GameManager grants that, and adding it
// just for a cheat would mean maintaining a second path into meta progression.
public static class SecretCodeCatalog
{
    public static readonly SecretCode[] Codes =
    {
        new()
        {
            Code = "INFINITIX",
            Reward = "+100 Dinero",
            Grant = gm => gm.AddLibras(100),
        },
        new()
        {
            Code = "PLASMA",
            Reward = "Color Plasma desbloqueado en todo",
            // The most expensive Epico colour (90 Libras a slot, eight slots) handed over in every
            // category at once — a genuinely useful thing to test the shop's owned/equipped states
            // with, since buying it legitimately would take 720 Libras.
            Grant = gm =>
            {
                foreach (CosmeticCategory category in Enum.GetValues<CosmeticCategory>())
                    gm.GrantCosmetic(category, "plasma");
            },
        },
        new()
        {
            Code = "TRIPULACION",
            Reward = "Piloto secreto desbloqueado",
            Grant = gm => gm.GrantCharacter("secreto1"),
        },
        new()
        {
            Code = "MDG",
            Reward = "Pilotos del equipo de trabajo desbloqueados",
            Grant = gm => gm.UnlockCoworkerRoster(),
        },
    };

    /// <summary>Upper-cased with all whitespace stripped — the form both lookup and the redeemed-set
    /// use, so "  infinitix " and "INFINITIX" are the same code and can't be redeemed twice.</summary>
    public static string Normalise(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";

        var builder = new System.Text.StringBuilder(input.Length);
        foreach (char c in input)
            if (!char.IsWhiteSpace(c)) builder.Append(char.ToUpperInvariant(c));
        return builder.ToString();
    }

    public static bool TryGet(string normalised, out SecretCode code)
    {
        foreach (var candidate in Codes)
        {
            if (candidate.Code == normalised)
            {
                code = candidate;
                return true;
            }
        }
        code = default;
        return false;
    }

    // Only used to seed a "codes you've already used" display; the game itself never needs the list.
    public static IEnumerable<string> AllCodes()
    {
        foreach (var code in Codes) yield return code.Code;
    }
}
