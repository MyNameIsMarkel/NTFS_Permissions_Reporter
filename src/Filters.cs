using System;
using System.Collections.Generic;

namespace NtfsReporter
{
    static class Ops
    {
        public const int BeginsWith = 0, Contains = 1, NotBeginsWith = 2, NotContains = 3, NotEndsWith = 4, EndsWith = 5, Is = 6, IsNot = 7;
        public static readonly string[] Names = {
            "Empieza por", "Contiene", "No empieza por", "No contiene", "No termina en", "Termina en", "Es", "No es" };
    }

    class FilterRule
    {
        public FieldDef Field;
        public int Op = Ops.Contains;
        public string Value = "";

        public bool Match(RowRef r)
        {
            string v = Field.Get(r) ?? "";
            string x = Value ?? "";
            switch (Op)
            {
                case Ops.BeginsWith: return v.StartsWith(x, StringComparison.OrdinalIgnoreCase);
                case Ops.Contains: return v.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0;
                case Ops.NotBeginsWith: return !v.StartsWith(x, StringComparison.OrdinalIgnoreCase);
                case Ops.NotContains: return v.IndexOf(x, StringComparison.OrdinalIgnoreCase) < 0;
                case Ops.NotEndsWith: return !v.EndsWith(x, StringComparison.OrdinalIgnoreCase);
                case Ops.EndsWith: return v.EndsWith(x, StringComparison.OrdinalIgnoreCase);
                case Ops.Is: return string.Equals(v, x, StringComparison.OrdinalIgnoreCase);
                case Ops.IsNot: return !string.Equals(v, x, StringComparison.OrdinalIgnoreCase);
            }
            return true;
        }

        public string Describe()
        {
            return Field.Name + " " + Ops.Names[Op].ToLowerInvariant() + " \"" + Value + "\"";
        }
    }

    class FilterSet
    {
        public readonly List<FilterRule> Rules = new List<FilterRule>();
        public bool MatchAll = true;
        public string QuickText = "";
        public List<FieldDef> QuickFields = new List<FieldDef>();

        public bool IsEmpty { get { return Rules.Count == 0 && string.IsNullOrEmpty(QuickText); } }

        public bool Match(RowRef r)
        {
            if (Rules.Count > 0)
            {
                bool ok;
                if (MatchAll)
                {
                    ok = true;
                    foreach (FilterRule f in Rules) if (!f.Match(r)) { ok = false; break; }
                }
                else
                {
                    ok = false;
                    foreach (FilterRule f in Rules) if (f.Match(r)) { ok = true; break; }
                }
                if (!ok) return false;
            }
            if (!string.IsNullOrEmpty(QuickText))
            {
                foreach (FieldDef f in QuickFields)
                {
                    string v = f.Get(r);
                    if (v != null && v.IndexOf(QuickText, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                return false;
            }
            return true;
        }
    }
}
