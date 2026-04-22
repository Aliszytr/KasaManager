using System;
using System.Collections.Generic;
using System.Linq;

namespace ParityRunner;

public class DiffEngine
{
    public record DiffResult(
        string Key,
        decimal Expected,
        decimal Actual,
        decimal Difference,
        bool IsMatch);
    
    public static List<DiffResult> Compare(
        ParitySnapshot expected, 
        ParitySnapshot actual,
        decimal tolerance = 0.01m)
    {
        var results = new List<DiffResult>();
        
        foreach (var key in expected.Sonuclar.Keys)
        {
            var exp = expected.Sonuclar[key];
            var act = actual.Sonuclar.GetValueOrDefault(key, 0m); // In case missing
            
            // Check missing with exact ContainsKey instead
            if (!actual.Sonuclar.ContainsKey(key))
            {
                act = 0m;
            }

            var diff = Math.Abs(exp - act);
            
            results.Add(new DiffResult(
                Key: key,
                Expected: exp,
                Actual: act,
                Difference: diff,
                IsMatch: diff <= tolerance));
        }
        
        // Yeni projede olup eski projede olmayan alanları da ekle
        foreach (var key in actual.Sonuclar.Keys.Except(expected.Sonuclar.Keys))
        {
            results.Add(new DiffResult(
                Key: key,
                Expected: 0m,
                Actual: actual.Sonuclar[key],
                Difference: Math.Abs(actual.Sonuclar[key]),
                IsMatch: false));
        }
        
        return results;
    }
}
