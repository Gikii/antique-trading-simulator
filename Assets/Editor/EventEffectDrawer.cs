using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using AntiqueTradingSimulator.Events;

namespace AntiqueTradingSimulator.EditorTools
{

    [CustomPropertyDrawer(typeof(EventEffect), true)]
    public class EventEffectDrawer : PropertyDrawer
    {
        private const float Spacing = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var typeRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            string currentName = property.managedReferenceValue?.GetType().Name ?? "<None — click to choose type>";

            if (EditorGUI.DropdownButton(typeRect, new GUIContent($"{label.text}: {currentName}"), FocusType.Keyboard))
            {
                ShowTypeMenu(property);
            }

            if (property.managedReferenceValue != null)
            {
                float y = typeRect.yMax + Spacing;
                EditorGUI.indentLevel++;

                var end = property.GetEndProperty();
                var child = property.Copy();
                bool enterChildren = true;
                while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
                {
                    enterChildren = false;
                    float h = EditorGUI.GetPropertyHeight(child, true);
                    var r = new Rect(position.x, y, position.width, h);
                    EditorGUI.PropertyField(r, child, true);
                    y += h + Spacing;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;

            if (property.managedReferenceValue != null)
            {
                height += Spacing;
                var end = property.GetEndProperty();
                var child = property.Copy();
                bool enterChildren = true;
                while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
                {
                    enterChildren = false;
                    height += EditorGUI.GetPropertyHeight(child, true) + Spacing;
                }
            }

            return height;
        }

        private void ShowTypeMenu(SerializedProperty property)
        {
            var menu = new GenericMenu();
            var currentType = property.managedReferenceValue?.GetType();

            menu.AddItem(new GUIContent("<None>"), currentType == null, () => SetType(property, null));

            var derivedTypes = TypeCache.GetTypesDerivedFrom<EventEffect>()
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition)
                .OrderBy(t => t.Name);

            foreach (var type in derivedTypes)
            {
                var capturedType = type;
                menu.AddItem(new GUIContent(type.Name), currentType == type, () => SetType(property, capturedType));
            }

            menu.ShowAsContext();
        }

        private void SetType(SerializedProperty property, Type type)
        {
            property.serializedObject.Update();
            property.managedReferenceValue = type == null ? null : Activator.CreateInstance(type);
            property.serializedObject.ApplyModifiedProperties();
        }
    }
}
