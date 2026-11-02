using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Dependency;
using UnityEditor;
using UnityEngine;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Draws a category as two dropdowns on one line: the dimension, then its enum member. The
    /// second list comes from the dimension's [FacetEnum] attribute.
    /// </summary>
    [CustomPropertyDrawer(typeof(MarketFacet))]
    public class MarketFacetDrawer : PropertyDrawer
    {
        private const float Gap = 4f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var kindProperty = property.FindPropertyRelative("Kind");
            var valueProperty = property.FindPropertyRelative("Value");

            if (kindProperty == null || valueProperty == null)
            {
                EditorGUI.LabelField(position, label.text, "MarketFacet: fields not found");
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            var contentRect = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            float halfWidth = (contentRect.width - Gap) * 0.5f;
            var kindRect = new Rect(contentRect.x, contentRect.y, halfWidth, contentRect.height);
            var valueRect = new Rect(contentRect.x + halfWidth + Gap, contentRect.y,
                                     contentRect.width - halfWidth - Gap, contentRect.height);

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            // ---- dimension
            BuildKindOptions(out var kindNames, out var kindValues);

            EditorGUI.BeginChangeCheck();
            int newKind = EditorGUI.IntPopup(kindRect, kindProperty.intValue, kindNames, kindValues);
            bool kindChanged = EditorGUI.EndChangeCheck();

            if (kindChanged)
                kindProperty.intValue = newKind;

            // ---- member of that dimension's enum
            var kind = (FacetKind)kindProperty.intValue;
            var enumType = MarketFacets.EnumTypeOf(kind);

            if (enumType == null)
            {
                // A dimension whose [FacetEnum] attribute names no enum: edit the raw int, so the
                // data stays reachable.
                valueProperty.intValue = EditorGUI.IntField(valueRect, valueProperty.intValue);
            }
            else
            {
                BuildValueOptions(kind, enumType, out var valueNames, out var valueValues);

                // Switching dimension leaves a value from the previous enum behind; snap it to a
                // member of the new one, so the dropdown shows a real selection.
                if (kindChanged && Array.IndexOf(valueValues, valueProperty.intValue) < 0 && valueValues.Length > 0)
                    valueProperty.intValue = valueValues[0];

                valueProperty.intValue = EditorGUI.IntPopup(valueRect, valueProperty.intValue, valueNames, valueValues);
            }

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }

        private static void BuildKindOptions(out string[] names, out int[] values)
        {
            var kinds = MarketFacets.AllKinds;
            names = new string[kinds.Count];
            values = new int[kinds.Count];

            for (int i = 0; i < kinds.Count; i++)
            {
                names[i] = kinds[i].ToString();
                values[i] = (int)kinds[i];
            }
        }

        private static void BuildValueOptions(FacetKind kind, Type enumType, out string[] names, out int[] values)
        {
            int[] rawValues = MarketFacets.ValuesOf(kind);
            var nameList = new List<string>(rawValues.Length);
            var valueList = new List<int>(rawValues.Length);

            foreach (int value in rawValues)
            {
                string name = Enum.GetName(enumType, value);
                nameList.Add(name ?? value.ToString());
                valueList.Add(value);
            }

            names = nameList.ToArray();
            values = valueList.ToArray();
        }
    }
}
