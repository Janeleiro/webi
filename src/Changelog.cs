namespace Webi;

/// <summary>One release: its version (as in project.godot), its date and what changed, in plain words for people.</summary>
public sealed record Release(string Version, string Date, string[] Changes);

/// <summary>
/// What changed in each version, newest first, shown on the "Novidades" page of the settings. A new version goes on
/// top here and in application/config/version (project.godot) together: a test checks they agree.
/// </summary>
public static class Changelog
{
    public static readonly Release[] Releases =
    {
        new("0.2.0", "2026-11-06", new[]
        {
            "Configurações numa página no navegador (botão direito na Webi → ⚙ Configurações...). O menu ficou só com as ações rápidas.",
            "Nova página \"Novidades\" (esta), com o que mudou em cada versão.",
            "Área de Trabalho Remota: a Webi reconhece a conexão remota (Área de Trabalho Remota e Windows App), comenta sobre ela e conta esse tempo no resumo do dia.",
            "Apps remotos (RemoteApp) são reconhecidos pelo fim do título, como \"Livro1 - Excel (Remoto)\", e ganham os comentários do próprio app.",
            "Dentro de uma área de trabalho remota ela puxa assunto mais vezes (a cada 5 a 10 min de uso), já que não enxerga os apps lá dentro.",
            "Prints de tela não têm mais reação: a fala saía na imagem ou trocava a fala que você queria registrar.",
            "Avisos de CPU, memória e bateria não se acumulam mais enquanto a Webi está quieta (Não Perturbe, reunião): fica só o mais recente de cada tipo, e se o problema já passou o aviso é descartado.",
        }),
        new("0.1.0", "2026-11-04", new[]
        {
            "Primeira versão: a aranha no desktop, andando sobre as janelas, tecendo teias e caçando moscas, com lembretes de água e pausa, pomodoro, monitor do sistema, post-its, timers, datas especiais, conversa, amizade e carona no mouse.",
        }),
    };
}
