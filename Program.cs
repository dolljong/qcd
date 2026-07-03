using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QCd;

internal static class Program
{
    private static string? _outFile;
    private static readonly string InstallDir = AppContext.BaseDirectory;
    private static string _rootsFile = "";

    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".svn", ".hg", ".vs", ".idea",
        "AppData", "$Recycle.Bin", "System Volume Information",
        "Windows", "$WinREAgent", "$SysReset", "Recovery",
        "Program Files", "Program Files (x86)", "ProgramData",
        "OneDriveTemp",
    };

    private const int MaxCandidatesPerLevel = 2000;

    private static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
        }
        catch { }

        _rootsFile = Path.Combine(InstallDir, "roots.txt");

        var positional = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "-h" or "--help") { PrintHelp(); return 0; }
            else if (a == "--out" && i + 1 < args.Length) { _outFile = args[++i]; }
            else if (a == "--list-roots") { ListRoots(); return 0; }
            else if (a == "--add-root" && i + 1 < args.Length) { AddRoot(args[++i]); return 0; }
            else if (a == "--remove-root" && i + 1 < args.Length) { RemoveRoot(args[++i]); return 0; }
            else positional.Add(a);
        }

        if (positional.Count == 0)
        {
            PrintHelp();
            return 0;
        }

        var query = string.Join(" ", positional).Trim();

        // Absolute path shortcut: qcd C:\some\path
        if (Path.IsPathFullyQualified(query))
        {
            if (Directory.Exists(query))
            {
                WriteResult(Path.GetFullPath(query));
                return 0;
            }
            Console.Error.WriteLine("경로가 존재하지 않습니다: " + query);
            return 1;
        }

        var segments = query
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        if (segments.Count == 0)
        {
            Console.Error.WriteLine("빈 쿼리입니다.");
            return 1;
        }

        var roots = LoadRoots();
        if (roots.Count == 0)
        {
            Console.Error.WriteLine($"검색 루트가 설정되지 않았습니다. {_rootsFile} 편집 또는 --add-root 사용.");
            return 1;
        }

        var scope = roots.Where(Directory.Exists).ToList();
        if (scope.Count == 0)
        {
            Console.Error.WriteLine("설정된 루트 중 존재하는 폴더가 없습니다.");
            return 1;
        }

        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            var found = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

            Parallel.ForEach(scope, start => SearchTree(start, seg, found));

            scope = found.Keys.ToList();
            if (scope.Count == 0)
            {
                Console.Error.WriteLine($"세그먼트 '{seg}'에 매칭되는 폴더가 없습니다.");
                return 1;
            }
            if (scope.Count > MaxCandidatesPerLevel)
            {
                Console.Error.WriteLine($"매칭이 너무 많습니다 ({scope.Count}개). 쿼리를 더 구체적으로.");
                return 1;
            }
        }

        var lastSeg = segments[^1];
        scope.Sort((a, b) =>
        {
            var an = Path.GetFileName(a.TrimEnd('\\', '/'));
            var bn = Path.GetFileName(b.TrimEnd('\\', '/'));
            int r = Rank(an, lastSeg) - Rank(bn, lastSeg);
            if (r != 0) return r;
            int lenDiff = a.Length - b.Length;
            if (lenDiff != 0) return lenDiff;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        });

        if (scope.Count == 1)
        {
            WriteResult(scope[0]);
            return 0;
        }

        int width = scope.Count.ToString().Length;
        for (int i = 0; i < scope.Count; i++)
        {
            Console.Error.WriteLine($" {(i + 1).ToString().PadLeft(width)}) {scope[i]}");
        }
        Console.Error.Write("선택 (엔터=취소): ");
        var line = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            Console.Error.WriteLine("취소됨.");
            return 0;
        }
        if (!int.TryParse(line.Trim(), out var idx) || idx < 1 || idx > scope.Count)
        {
            Console.Error.WriteLine("잘못된 선택입니다.");
            return 1;
        }
        WriteResult(scope[idx - 1]);
        return 0;
    }

    private static int Rank(string name, string term)
    {
        if (name.Equals(term, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return 1;
        return 2;
    }

    private static void SearchTree(string start, string term, ConcurrentDictionary<string, byte> results)
    {
        start = start.TrimEnd('\\', '/');
        if (start.Length == 2 && start[1] == ':') start += "\\"; // "C:" -> "C:\"

        var startName = Path.GetFileName(start.TrimEnd('\\', '/'));
        if (!string.IsNullOrEmpty(startName) &&
            startName.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            results.TryAdd(start.TrimEnd('\\', '/'), 0);
        }

        var stack = new Stack<string>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            if (results.Count > MaxCandidatesPerLevel) return;

            var current = stack.Pop();
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(current); }
            catch { continue; }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (string.IsNullOrEmpty(name)) continue;
                if (Excluded.Contains(name)) continue;

                FileAttributes attrs;
                try { attrs = File.GetAttributes(child); }
                catch { continue; }
                if ((attrs & FileAttributes.ReparsePoint) != 0) continue;

                if (name.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    results.TryAdd(child.TrimEnd('\\', '/'), 0);
                }
                stack.Push(child);
            }
        }
    }

    private static List<string> LoadRoots()
    {
        if (!File.Exists(_rootsFile))
        {
            var defaults = new[]
            {
                "# qcd search roots — one absolute path per line, # for comments",
                @"C:\project",
                @"C:\dev",
            };
            Directory.CreateDirectory(Path.GetDirectoryName(_rootsFile)!);
            File.WriteAllLines(_rootsFile, defaults, new UTF8Encoding(false));
        }
        return File.ReadAllLines(_rootsFile)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("#"))
            .ToList();
    }

    private static void ListRoots()
    {
        var roots = LoadRoots();
        Console.WriteLine($"roots.txt: {_rootsFile}");
        if (roots.Count == 0)
        {
            Console.WriteLine("  (비어있음)");
            return;
        }
        foreach (var r in roots)
        {
            var mark = Directory.Exists(r) ? " " : "!";
            Console.WriteLine($"  {mark} {r}");
        }
    }

    private static void AddRoot(string dir)
    {
        var full = Path.GetFullPath(dir);
        var roots = LoadRoots();
        if (roots.Any(r => r.Equals(full, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("이미 존재: " + full);
            return;
        }
        var allLines = File.Exists(_rootsFile)
            ? File.ReadAllLines(_rootsFile).ToList()
            : new List<string>();
        allLines.Add(full);
        File.WriteAllLines(_rootsFile, allLines, new UTF8Encoding(false));
        Console.WriteLine("추가됨: " + full);
    }

    private static void RemoveRoot(string dir)
    {
        var full = Path.GetFullPath(dir);
        if (!File.Exists(_rootsFile))
        {
            Console.WriteLine("roots.txt 없음.");
            return;
        }
        var lines = File.ReadAllLines(_rootsFile).ToList();
        var before = lines.Count;
        lines = lines.Where(l =>
        {
            var t = l.Trim();
            if (t.Length == 0 || t.StartsWith("#")) return true;
            return !t.Equals(full, StringComparison.OrdinalIgnoreCase);
        }).ToList();
        if (lines.Count == before)
        {
            Console.WriteLine("찾을 수 없음: " + full);
            return;
        }
        File.WriteAllLines(_rootsFile, lines, new UTF8Encoding(false));
        Console.WriteLine("제거됨: " + full);
    }

    private static void WriteResult(string path)
    {
        path = path.TrimEnd('\\', '/');
        if (_outFile != null)
        {
            File.WriteAllText(_outFile, path + "\r\n", new UTF8Encoding(false));
        }
        else
        {
            Console.WriteLine(path);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("qcd — 폴더명 패턴으로 빠르게 이동");
        Console.WriteLine();
        Console.WriteLine("사용법:");
        Console.WriteLine("  qcd <query>                   폴더명에 query가 포함된 폴더로 이동");
        Console.WriteLine("  qcd <seg1>/<seg2>[/...]       계층 힌트 (앞이 뒷 검색범위)");
        Console.WriteLine("  qcd --list-roots              검색 루트 목록 표시");
        Console.WriteLine("  qcd --add-root <dir>          검색 루트 추가");
        Console.WriteLine("  qcd --remove-root <dir>       검색 루트 제거");
        Console.WriteLine("  qcd --help                    도움말");
        Console.WriteLine();
        Console.WriteLine("예:");
        Console.WriteLine("  qcd proj                      'proj' 포함 폴더");
        Console.WriteLine("  qcd proj/영동대로              proj 아래에서 '영동대로' 포함");
        Console.WriteLine();
        Console.WriteLine("설정 파일: roots.txt (실행 파일과 같은 폴더)");
    }
}
