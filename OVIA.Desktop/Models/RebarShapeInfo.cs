using System;
using System.Collections.Generic;

namespace OVIA.Desktop
{
    public class RebarShapeInfo
    {
        public string ShapeCode { get; set; }
        public int ShapeNo { get; set; }
        public string ShapeName { get; set; }
        public string Category { get; set; }
        public string FieldsText { get; set; }
        public string OptionText { get; set; }
        public bool IsUserSelectable { get; set; }
        public string ApproveStatus { get; set; }
        public string SourceImagePath { get; set; }
        public string RefSvgPath { get; set; }
        public string CleanSvgPath { get; set; }
        public string VectorStatus { get; set; }
        public List<RebarShapeCommand> Commands { get; private set; }

        public RebarShapeInfo()
        {
            ShapeCode = "";
            ShapeName = "";
            Category = "";
            FieldsText = "";
            OptionText = "";
            ApproveStatus = "APPROVED";
            IsUserSelectable = true;
            SourceImagePath = "";
            RefSvgPath = "";
            CleanSvgPath = "";
            VectorStatus = "";
            Commands = new List<RebarShapeCommand>();
        }

        public string DisplayCode
        {
            get { return ShapeNo <= 0 ? "" : ShapeNo.ToString(); }
        }

        public string DisplayName
        {
            get
            {
                if (ShapeNo <= 0)
                {
                    return "이미지 없음";
                }

                string name = ShapeName == null ? "" : ShapeName.Trim();

                if (name == "")
                {
                    name = "형상 " + DisplayCode;
                }

                return DisplayCode + " - " + name;
            }
        }

        public bool HasCommandVector
        {
            get { return Commands != null && Commands.Count > 0; }
        }

        public List<string> GetFieldKeys()
        {
            List<string> list = new List<string>();
            string text = FieldsText == null ? "" : FieldsText.Trim();

            if (text == "")
            {
                return list;
            }

            string[] parts = text.Replace(",", "|").Replace("/", "|").Split('|');
            int i;

            for (i = 0; i < parts.Length; i++)
            {
                string key = NormalizeFieldKey(parts[i]);

                if (key != "" && !ContainsField(list, key))
                {
                    list.Add(key);
                }
            }

            return list;
        }

        public bool HasField(string fieldKey)
        {
            string key = NormalizeFieldKey(fieldKey);
            List<string> fields = GetFieldKeys();
            int i;

            for (i = 0; i < fields.Count; i++)
            {
                if (fields[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public int GetLengthFieldCount()
        {
            List<string> fields = GetFieldKeys();
            int count = 0;
            int i;

            for (i = 0; i < fields.Count; i++)
            {
                string key = fields[i];

                if (key == "A" || key == "B" || key == "C" || key == "D" || key == "E" || key == "F" || key == "G" || key == "H")
                {
                    count++;
                }
            }

            return count;
        }

        public bool HasOption(string optionKey)
        {
            if (optionKey == null)
            {
                return false;
            }

            string option = optionKey.Trim().ToUpperInvariant();
            string text = OptionText == null ? "" : OptionText.Trim().ToUpperInvariant();

            if (option == "")
            {
                return false;
            }

            if (text.IndexOf(option, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (option == "ROUND")
            {
                return HasField("R1") || HasField("R2") || HasField("R3");
            }

            return false;
        }

        private bool ContainsField(List<string> list, string key)
        {
            int i;

            for (i = 0; i < list.Count; i++)
            {
                if (list[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static string NormalizeFieldKey(string value)
        {
            if (value == null)
            {
                return "";
            }

            string key = value.Trim().ToUpperInvariant();
            key = key.Replace(" ", "");
            key = key.Replace("값", "");

            if (key == "")
            {
                return "";
            }

            if (key == "R")
            {
                return "R1";
            }

            if (key == "A" || key == "B" || key == "C" || key == "D" || key == "E" || key == "F" || key == "G" || key == "H" || key == "X" || key == "Y" || key == "Z")
            {
                return key;
            }

            if (key == "R1" || key == "R2" || key == "R3" || key == "R4")
            {
                return key;
            }

            return key;
        }
    }
}
