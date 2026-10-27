using System;
using System.Globalization;
using Webi.Spider;

namespace Webi.Assistant;

/// <summary>What Webi wears on her head.</summary>
public enum Hat { None, Witch, Santa, Party, Straw, Bunny, Heart }

/// <summary>A special day: the hat of the season and what Webi says about it.</summary>
public readonly record struct SpecialDay(string Id, Hat Hat, Reaction? Greeting, string[] Lines);

/// <summary>
/// The calendar: Brazilian holidays and dates (carnival and Easter are computed), the seasons that get a hat
/// (Halloween week, December, festa junina) and the user's birthday. Pure logic, so it is tested.
/// </summary>
public static class Seasons
{
    /// <summary>Reads "dd/mm" (also "dd-mm" or "dd.mm"); false for anything else.</summary>
    public static bool TryParseBirthday(string text, out int day, out int month)
    {
        day = month = 0;
        var parts = text.Trim().Split('/', '-', '.');
        if (parts.Length < 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out day)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out month)) return false;
        if (month is < 1 or > 12) return false;
        return day >= 1 && day <= DateTime.DaysInMonth(2024, month); // leap year: 29/02 is allowed
    }

    /// <summary>Easter Sunday (anonymous Gregorian algorithm).</summary>
    public static DateTime Easter(int year)
    {
        int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4;
        int f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31, day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateTime(year, month, day);
    }

    /// <summary>The special day for <paramref name="date"/>, if any. The birthday wins over everything else.</summary>
    public static SpecialDay? Today(DateTime date, string birthday)
    {
        var d = date.Date;
        if (TryParseBirthday(birthday, out int bd, out int bm) && d.Day == bd && d.Month == bm)
            return new("birthday", Hat.Party,
                new("FELIZ ANIVERSÁRIO!!! 🎂🎉 Hoje é o seu dia! Teci uma teia em sua homenagem (de mentirinha).", Face.Happy, Gesture.Dance),
                new[]
                {
                    "Parabéns pra você, nesta data querida... 🎶🕷️",
                    "Aniversariante do dia não trabalha muito, né? 🎂",
                    "Já comeu bolo hoje? É obrigatório no aniversário!",
                    "Oito patinhas batendo palmas pra você! 👏👏👏👏",
                });

        var easter = Easter(d.Year);
        var carnival = easter.AddDays(-47); // Tuesday
        int md = d.Month * 100 + d.Day;

        if (md == 1031)
            return new("halloween", Hat.Witch,
                new("FELIZ HALLOWEEN!!! 🎃 Hoje é o meu dia: finalmente todo mundo gosta de aranha!", Face.Happy, Gesture.Dance),
                HalloweenLines);
        if (md is >= 1024 and <= 1030)
            return new("halloween-week", Hat.Witch, null, HalloweenLines);
        if (md == 1225)
            return new("christmas", Hat.Santa,
                new("FELIZ NATAL!!! 🎄 Pendurei oito meias. Uma pra cada pata.", Face.Happy, Gesture.Dance), ChristmasLines);
        if (md == 1224)
            return new("christmas-eve", Hat.Santa,
                new("Hoje é véspera de Natal! 🎄 Já tem ceia? Eu trouxe... uma mosca. É de coração.", Face.Happy, Gesture.Wave), ChristmasLines);
        if (md == 1231)
            return new("new-year-eve", Hat.Party,
                new("Último dia do ano! 🎆 Quais são as metas pro ano que vem? A minha: mais teias.", Face.Happy, Gesture.Dance), NewYearLines);
        if (md == 101)
            return new("new-year", Hat.Party,
                new("FELIZ ANO NOVO!!! 🎆 Que esse ano tenha muita água, pausas e pouca memória lotada.", Face.Happy, Gesture.Dance), NewYearLines);
        if (md is >= 1201 and <= 1230)
            return new("december", Hat.Santa, null, ChristmasLines);
        if (d >= carnival.AddDays(-3) && d <= carnival.AddDays(1))
            return new("carnival", Hat.Party,
                d == carnival.AddDays(-3) ? new("É CARNAVAL!!! 🎭 Vou de fantasia de aranha. Ah, espera...", Face.Happy, Gesture.Dance) : null,
                new[] { "Ô abre alas que eu quero passar! 🎶🕷️", "Carnaval é com oito patas sambando! 💃", "Confete é tipo teia colorida, né?" });
        if (d == easter)
            return new("easter", Hat.Bunny,
                new("Feliz Páscoa! 🐰🍫 Me disseram que tinha ovo de chocolate. Pra aranha tem?", Face.Happy, Gesture.Wave),
                new[] { "Coelhinho da Páscoa, que trazes pra mim? Uma mosca de chocolate? 🍫", "Ganhou ovo de Páscoa? Divide comigo? 🥚" });
        if (md == 612)
            return new("valentines", Hat.Heart,
                new("Feliz Dia dos Namorados! 💘 Se tiver alguém especial, manda um oi. Se não, tem eu!", Face.Happy, Gesture.Wave),
                new[] { "Aranhas são ótimas em criar laços. Literalmente. 💘", "Te amo mais que mosca presa na teia. 💕" });
        if (d.Month == 6 || (d.Month == 7 && d.Day <= 15))
            return new("festa-junina", Hat.Straw,
                md == 624 ? new("Viva São João!!! 🔥🌽 Arraiá da Webi aberto!", Face.Happy, Gesture.Dance) : null,
                new[]
                {
                    "Olha a cobra! É mentira! 🐍 (Olha a aranha: é verdade.)",
                    "Já comeu pamonha esse mês? 🌽",
                    "Quadrilha com oito patas é outra coisa! 💃",
                    "Anarriê! 🎶",
                });

        (string id, string text)? simple = md switch
        {
            421 => ("tiradentes", "Hoje é Tiradentes! Feriado bom é feriado com descanso. 🇧🇷"),
            501 => ("labour", "Dia do Trabalhador! Hoje a única teia que eu teço é a da rede de descanso. 😴"),
            907 => ("independence", "Feliz 7 de Setembro! 🇧🇷 Independência ou... soneca!"),
            1012 => ("children", "Dia das Crianças! 🧸 Toda aranha tem uma criança interior. A minha quer pular!"),
            1102 => ("finados", "Hoje é Finados. Um dia pra lembrar com carinho de quem já foi. 🕯️"),
            1115 => ("republic", "Feriado da Proclamação da República! Aproveita o descanso. 🇧🇷"),
            1120 => ("consciencia", "Dia da Consciência Negra. ✊🏿 Um dia de memória e respeito."),
            _ => null,
        };
        if (simple is { } s)
            return new(s.id, Hat.None, new(s.text, s.id == "finados" ? Face.Normal : Face.Happy, s.id == "finados" ? Gesture.None : Gesture.Wave),
                Array.Empty<string>());

        if (d.DayOfWeek == DayOfWeek.Friday)
            return new("friday", Hat.None, new("SEXTOU!!! 🎉 Falta pouco pro fim de semana!", Face.Happy, Gesture.Dance),
                new[] { "Sextou! Hoje até as moscas estão mais animadas. 🎉", "Sexta-feira: o dia oficial da dancinha. 💃" });
        if (d.DayOfWeek == DayOfWeek.Monday)
            return new("monday", Hat.None, new("Segunda-feira... 😮‍💨 Bora, uma pata de cada vez. Eu tenho oito, te empresto.", Face.Sleepy, Gesture.Stretch),
                new[] { "Segunda é só a sexta do avesso. Força! 💪", "Café na segunda é tipo seda na teia: essencial. ☕" });
        if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return new("weekend", Hat.None, null,
                new[] { "Fim de semana no PC? Tô aqui fazendo companhia. 🕷️", "Não esquece de descansar um pouco, é fim de semana!" });
        return null;
    }

    static readonly string[] HalloweenLines =
    {
        "Gostosuras ou travessuras? 🎃 Eu aceito mosca.",
        "Essa é a época do ano em que eu sou a decoração oficial. 🕸️",
        "Bu! 👻 Assustei? Não? Poxa...",
        "Meu chapéu de bruxa combina com as minhas oito patas, né? 🧙",
        "Todo mundo comprando aranha de plástico... e a original aqui, de graça! 🕷️",
    };

    static readonly string[] ChristmasLines =
    {
        "Ho ho ho! 🎅 Fui boazinha esse ano. Quase sempre.",
        "Teia de Natal: igual pisca-pisca, mas sem eletricidade. 🎄",
        "Pedi pro Papai Noel um mousepad de seda. 🎁",
        "Já montou a árvore? Eu moro numa, tecnicamente. 🌲",
    };

    static readonly string[] NewYearLines =
    {
        "Meta do ano: beber mais água. A sua também, viu? 💧",
        "Ano novo, teia nova! 🎆",
        "Pular sete ondas? Eu pulo sete janelas. 🌊",
    };
}
