using System;
using System.IO;
using System.Text;
using Ttfx.Effects;

namespace Ttfx.Cli;

/// <summary>
/// Hand-written bash/zsh completion scripts driven by <see cref="EffectRegistry"/>.
/// Exact clap_complete text is not required; scripts must be non-empty, syntactically
/// valid, and mention every registry effect name.
/// </summary>
public static class Completions
{
    public static void Print(string shell, TextWriter stdout)
    {
        if (shell == "bash")
        {
            PrintBash(stdout);
            return;
        }

        PrintZsh(stdout);
    }

    private static void PrintBash(TextWriter stdout)
    {
        var sb = new StringBuilder();
        sb.AppendLine("_ttfx() {");
        sb.AppendLine("    local cur prev");
        sb.AppendLine("    COMPREPLY=()");
        sb.AppendLine("    cur=\"${COMP_WORDS[COMP_CWORD]}\"");
        sb.AppendLine("    prev=\"${COMP_WORDS[COMP_CWORD-1]}\"");
        sb.AppendLine("    case \"${prev}\" in");
        sb.AppendLine("        --print-completion|--include-effects|--exclude-effects)");
        sb.AppendLine("            COMPREPLY=( $(compgen -W \"bash zsh\" -- \"$cur\") )");
        sb.AppendLine("            return 0");
        sb.AppendLine("            ;;");
        sb.AppendLine("        --existing-color-handling)");
        sb.AppendLine("            COMPREPLY=( $(compgen -W \"always dynamic ignore\" -- \"$cur\") )");
        sb.AppendLine("            return 0");
        sb.AppendLine("            ;;");
        sb.AppendLine("        --anchor-canvas|--anchor-text)");
        sb.AppendLine("            COMPREPLY=( $(compgen -W \"sw s se e ne n nw w c\" -- \"$cur\") )");
        sb.AppendLine("            return 0");
        sb.AppendLine("            ;;");
        sb.AppendLine("    esac");
        sb.AppendLine("    if [[ ${COMP_CWORD} -eq 1 ]]; then");
        sb.AppendLine("        COMPREPLY=( $(compgen -W \"");
        AppendEffectNames(sb, ' ');
        sb.AppendLine("\" -- \"$cur\") )");
        sb.AppendLine("        COMPREPLY+=( $(compgen -W \"");
        AppendRootFlags(sb, ' ');
        sb.AppendLine("\" -- \"$cur\") )");
        sb.AppendLine("    fi");
        sb.AppendLine("}");
        sb.AppendLine("complete -F _ttfx ttfx");
        stdout.Write(sb.ToString());
    }

    private static void PrintZsh(TextWriter stdout)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#compdef ttfx");
        sb.AppendLine();
        sb.AppendLine("_ttfx() {");
        sb.AppendLine("    local context state line");
        sb.AppendLine("    _arguments -C \\");
        sb.AppendLine("        '(-v --version)'{-v,--version}'[Print the version and exit]' \\");
        sb.AppendLine("        '(-i --input-file)'{-i,--input-file}'[File to read input from]:file:_files' \\");
        sb.AppendLine("        '--tab-width[Tab width]:integer:' \\");
        sb.AppendLine("        '--xterm-colors[Use xterm 256-color palette]' \\");
        sb.AppendLine("        '--no-color[Disable color output]' \\");
        sb.AppendLine("        '--terminal-background-color[Terminal background color]:color:' \\");
        sb.AppendLine("        '--existing-color-handling[Existing color handling]:handling:(always dynamic ignore)' \\");
        sb.AppendLine("        '--wrap-text[Wrap input text]' \\");
        sb.AppendLine("        '--frame-rate[Frame rate]:integer:' \\");
        sb.AppendLine("        '--canvas-width[Canvas width]:integer:' \\");
        sb.AppendLine("        '--canvas-height[Canvas height]:integer:' \\");
        sb.AppendLine("        '--anchor-canvas[Canvas anchor]:anchor:(sw s se e ne n nw w c)' \\");
        sb.AppendLine("        '--anchor-text[Text anchor]:anchor:(sw s se e ne n nw w c)' \\");
        sb.AppendLine("        '--ignore-terminal-dimensions[Ignore terminal dimensions]' \\");
        sb.AppendLine("        '--reuse-canvas[Reuse canvas between frames]' \\");
        sb.AppendLine("        '--no-eol[Do not emit trailing newline]' \\");
        sb.AppendLine("        '--no-restore-cursor[Do not restore cursor on exit]' \\");
        sb.AppendLine("        '--seed[Random seed]:seed:' \\");
        sb.AppendLine("        '--print-completion[Print shell completion script]:shell:(bash zsh)' \\");
        sb.AppendLine("        '(-R --random-effect)'{-R,--random-effect}'[Run a random effect]' \\");
        sb.AppendLine("        '--include-effects[Limit random-effect selection]:effect:->effects' \\");
        sb.AppendLine("        '--exclude-effects[Exclude effects from random-effect selection]:effect:->effects' \\");
        sb.AppendLine("        '*:effect:(");
        AppendEffectNames(sb, '\n');
        sb.AppendLine(")'");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("compdef _ttfx ttfx");
        stdout.Write(sb.ToString());
    }

    private static void AppendEffectNames(StringBuilder sb, char separator)
    {
        EffectSpec[] effects = EffectRegistry.Effects;
        for (int i = 0; i < effects.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(separator);
            }

            sb.Append(effects[i].Name);
        }
    }

    private static void AppendRootFlags(StringBuilder sb, char separator)
    {
        string[] flags =
        [
            "-h", "--help", "-v", "--version", "-i", "--input-file", "--tab-width",
            "--xterm-colors", "--no-color", "--terminal-background-color",
            "--existing-color-handling", "--wrap-text", "--frame-rate",
            "--canvas-width", "--canvas-height", "--anchor-canvas", "--anchor-text",
            "--ignore-terminal-dimensions", "--reuse-canvas", "--no-eol",
            "--no-restore-cursor", "--seed", "--print-completion", "-R",
            "--random-effect", "--include-effects", "--exclude-effects",
        ];
        for (int i = 0; i < flags.Length; i++)
        {
            sb.Append(separator);
            sb.Append(flags[i]);
        }
    }
}
