using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AiInterview.Domain.Entities;

namespace AiInterview.Application.Services;

/// <summary>Chooses varied assessment targets; the configured AI still writes every actual question.</summary>
public static class QuestionPlanning
{
    public sealed record Target(string Topic, string Format);
    public static Target Next(InterviewSession session, int mainCount)
    {
        string[] topics = session.Kind switch
        {
            InterviewKind.Behavioral => ["team disagreement", "receiving feedback", "prioritizing under a deadline", "learning an unfamiliar tool", "owning a mistake", "clarifying ambiguous requirements", "helping a teammate", "communicating a technical risk", "responding to a failed delivery", "reflecting on a project"],
            InterviewKind.Project => ["project requirements and personal contribution", "architecture decision and alternatives", "hardest defect and investigation", "testing strategy and evidence", "data modeling and integrity", "security and access control", "performance measurement", "team collaboration and delivery", "deployment and monitoring", "lessons and next improvement"],
            InterviewKind.Coding => ["hash maps and lookup", "string parsing and validation", "sorting and intervals", "stacks and queues", "two pointers", "binary search", "linked structures", "tree traversal", "error handling and boundary cases", "batch processing and complexity"],
            _ => PositionTopics(session.Position.Name)
        };
        // A stable permutation per session survives retries while varying between practice sessions.
        var seed = SHA256.HashData(session.Id.ToByteArray());
        var ordered = topics.Select((topic, i) => new { topic, rank = seed[i] }).OrderBy(x => x.rank).Select(x => x.topic).ToArray();
        string[] formats = ["concrete workplace scenario", "design a small example and explain why", "compare alternatives with a tradeoff", "investigate a failure step by step", "explain a concept through an example"];
        return new(ordered[Math.Clamp(mainCount, 0, ordered.Length - 1)], formats[(mainCount + seed[12]) % formats.Length]);
    }

    private static string[] PositionTopics(string position) => position switch
    {
        "QA/Tester" => ["equivalence partitioning and boundary value test design", "bug report and severity versus priority", "REST API testing and status codes", "SQL data validation and test data", "regression scope and risk-based test selection", "exploratory testing and edge cases", "test automation and stable selectors", "requirements ambiguity and acceptance criteria", "integration testing and environment issues", "test planning and release confidence"],
        "Frontend" => ["semantic HTML and accessibility", "responsive CSS layout", "JavaScript async and event loop", "component state and data flow", "forms and validation", "HTTP requests and error states", "browser performance", "frontend testing", "XSS and safe rendering", "cross-browser debugging"],
        "Mobile" => ["screen lifecycle and state", "offline storage and synchronization", "network failures and retries", "navigation and user experience", "permissions and privacy", "battery and memory usage", "mobile testing", "notifications and background work", "responsive layouts", "release and crash diagnosis"],
        "Data" => ["SQL joins and aggregation", "data cleaning and missing values", "exploratory data analysis", "sampling and statistical bias", "data pipelines and validation", "visualization choices", "feature leakage and evaluation", "data modeling", "reproducibility", "privacy and access to data"],
        "DevOps" => ["Linux process and log diagnosis", "CI pipeline quality gates", "containers and image construction", "networking and DNS", "configuration and secrets", "monitoring and incident response", "deployment and rollback", "infrastructure automation", "backup and recovery", "service reliability and capacity"],
        "Fullstack" => ["HTTP request lifecycle", "frontend state and forms", "REST API contracts", "relational data modeling", "authentication and authorization", "end-to-end testing", "caching and performance", "error handling across layers", "deployment configuration", "Git collaboration"],
        _ => ["C# types and object-oriented design", "ASP.NET Core dependency injection and lifetimes", "REST API validation and errors", "SQL joins and indexes", "EF Core queries and performance", "authentication and resource authorization", "async await and cancellation", "transactions and concurrency", "unit and integration testing", "Git workflow and deployment diagnostics"]
    };

    public static bool IsNearDuplicate(string candidate, IEnumerable<string> previous)
    {
        var words = Normalize(candidate);
        foreach (var question in previous)
        {
            var other = Normalize(question);
            if (words == other && words.Length > 0) return true;
            var left = Grams(words); var right = Grams(other);
            if (left.Count < 8 || right.Count < 8) continue;
            var common = left.Intersect(right).Count();
            if ((double)common / (left.Count + right.Count - common) >= .72) return true;
        }
        return false;
    }
    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var plain = new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant().Replace('đ', 'd');
        return string.Join(' ', Regex.Matches(plain, @"[\p{L}\p{N}]+").Select(m => m.Value));
    }
    private static HashSet<string> Grams(string value)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Enumerable.Range(0, Math.Max(0, words.Length - 2)).Select(i => string.Join(' ', words.Skip(i).Take(3))).ToHashSet(StringComparer.Ordinal);
    }
}
