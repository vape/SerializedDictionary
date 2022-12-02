using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace SerializedDict.Editor
{
    [CustomPropertyDrawer(typeof(SerializedDictionaryDrawable), useForChildren: true)]
    public class SerialziedDictionaryDrawer : PropertyDrawer
    {
        private static readonly FieldInfo serializedObjectPtr = typeof(SerializedObject).GetField("m_NativeObjectPtr", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly GUIContent genericKeyLabel = new GUIContent("Key");
        private static readonly GUIContent dictionaryIsEmptyLabel = new GUIContent("Dictionary is empty");

        private struct Warning
        {
            public int Index;
            public string Text;
            public MessageType Type;
        }

        private class DictionaryState
        {
            public ReorderableList List;
            public Dictionary<int, List<Warning>> Warnings;
            public int NotAccessedCounter;

            public List<Warning> GetWarnings(int index)
            {
                if (Warnings == null || !Warnings.ContainsKey(index))
                {
                    return emptyWarnings;
                }

                return Warnings[index];
            }
        }

        private static readonly List<Warning> emptyWarnings = new List<Warning>();
        private static Dictionary<int, DictionaryState> states = new Dictionary<int, DictionaryState>();
        private static List<int> statesToRemove = new List<int>();

        private static void ClearUnusedStates()
        {
            foreach (var kv in states)
            {
                if (kv.Value.NotAccessedCounter > 60)
                {
                    statesToRemove.Add(kv.Key);
                }

                kv.Value.NotAccessedCounter++;
            }

            foreach (var key in statesToRemove)
            {
                states.Remove(key);
            }

            statesToRemove.Clear();
        }

        private static int GetStateKey(SerializedProperty property)
        {
            int hash = 17;
            hash = hash * 23 + property.serializedObject.targetObject.GetInstanceID();
            hash = hash * 23 + property.propertyPath.GetHashCode();

            return hash;
        }

        private static bool TryGetState(SerializedProperty property, out DictionaryState state)
        {
            return states.TryGetValue(GetStateKey(property), out state);
        }

        private static DictionaryState GetState(SerializedProperty property)
        {
            var key = GetStateKey(property);

            if (!states.TryGetValue(key, out var state))
            {
                state = new DictionaryState();
                states.Add(key, state);
            }

            state.NotAccessedCounter = 0;

            return state;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var state = GetState(property);
            ClearUnusedStates();

            if (state.Warnings == null)
            {
                state.Warnings = Validate(property);
            }

            if (state.List == null)
            {
                state.List = CreateList(property, state.GetWarnings);
            }
            else
            {
                var ptr = (IntPtr)serializedObjectPtr.GetValue(state.List.serializedProperty.serializedObject);

                // check if serialized object was disposed
                if (((int)ptr) == 0x0)
                {
                    state.List = CreateList(property, state.GetWarnings);
                }
            }

            var undoOrRedo =
                Event.current.type == EventType.KeyUp &&
              ((Event.current.modifiers & EventModifiers.Control) != 0 || (Event.current.modifiers & EventModifiers.Command) != 0) &&
               (Event.current.keyCode == KeyCode.Z || Event.current.keyCode == KeyCode.Y);

            using (var changeScope = new EditorGUI.ChangeCheckScope())
            {
                state.List.DoList(position);

                if (undoOrRedo || changeScope.changed)
                {
                    state.Warnings = Validate(property);

                    property.serializedObject.ApplyModifiedProperties();
                    property.serializedObject.Update();
                }
            }
        }

        private static Dictionary<int, List<Warning>> Validate(SerializedProperty property)
        {
            var result = new Dictionary<int, List<Warning>>();

            foreach (var warning in FindWarnings(property))
            {
                if (!result.ContainsKey(warning.Index))
                {
                    result.Add(warning.Index, new List<Warning>());
                }

                result[warning.Index].Add(warning);
            }

            return result;
        }

        private static IEnumerable<Warning> FindWarnings(SerializedProperty property)
        {
            var duplicates = new HashSet<int>();
            var keys = property.FindPropertyRelative("_keys");

            for (int i = 0; i < keys.arraySize; ++i)
            {
                var k0 = keys.GetArrayElementAtIndex(i);

                if (k0.propertyType == SerializedPropertyType.ObjectReference && k0.objectReferenceValue == null)
                {
                    yield return new Warning() { Index = i, Text = "Invalid key", Type = MessageType.Error };
                }

                for (int k = i + 1; k < keys.arraySize; ++k)
                {
                    if (!duplicates.Contains(k))
                    {
                        var k1 = keys.GetArrayElementAtIndex(k);

                        if (SerializedProperty.DataEquals(k0, k1))
                        {
                            if (k0.propertyType == SerializedPropertyType.String && k0.stringValue != k1.stringValue)
                            {
                                continue;
                            }

                            duplicates.Add(k);
                            yield return new Warning() { Index = k, Text = "Duplicate key", Type = MessageType.Error };
                        }
                    }
                }
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            TryGetState(property, out var state);

            var keys = property.FindPropertyRelative("_keys");
            var values = property.FindPropertyRelative("_values");
            var height =
                values.arraySize == 0 ?
                EditorGUIUtility.singleLineHeight * 3 + EditorGUIUtility.standardVerticalSpacing * 7 :
                EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing * 5;

            if (!ShouldDisplayHeader(property))
            {
                height -= EditorGUIUtility.singleLineHeight;
            }

            for (int i = 0; i < values.arraySize; ++i)
            {
                height += CalculateEntryHeight(keys.GetArrayElementAtIndex(i), values.GetArrayElementAtIndex(i), state == null ? 0 : state.GetWarnings(i).Count);
                height += EditorGUIUtility.standardVerticalSpacing;
            }

            return height;
        }

        public override bool CanCacheInspectorGUI(SerializedProperty property)
        {
            return false;
        }

        private static ReorderableList CreateList(SerializedProperty property, Func<int, List<Warning>> getWarnings)
        {
            var list = new ReorderableList(
                property.serializedObject,
                property.FindPropertyRelative("_keys"),
                draggable: true,
                displayHeader: ShouldDisplayHeader(property),
                displayAddButton: true,
                displayRemoveButton: true);

            list.multiSelect = true;
            list.drawElementCallback = (rect, index, active, focused) => OnDrawListElement(property, rect, index, active, focused, getWarnings(index));
            list.drawNoneElementCallback = OnDrawEmpty;
            list.onReorderCallbackWithDetails = (list, oldIndex, newIndex) => OnReorder(property, oldIndex, newIndex);
            list.onAddCallback = (list) => OnAdded(property);
            list.onRemoveCallback = (list) => OnRemoved(property, list);
            list.elementHeightCallback = (index) => OnCalculateElementHeight(property, index, getWarnings(index).Count);
            list.drawHeaderCallback = (rect) => OnDrawHeader(property, rect);
            return list;
        }

        private static bool ShouldDisplayHeader(SerializedProperty property)
        {
            return property.depth == 0;
        }

        private static void OnDrawHeader(SerializedProperty property, Rect rect)
        {
            EditorGUI.LabelField(rect, property.displayName);
        }

        private static float OnCalculateElementHeight(SerializedProperty property, int index, int warnings)
        {
            if (property == null)
            {
                return 0f;
            }

            var key = property.FindPropertyRelative("_keys").GetArrayElementAtIndex(index);
            var value = property.FindPropertyRelative("_values").GetArrayElementAtIndex(index);

            return CalculateEntryHeight(key, value, warnings);
        }

        private static float CalculateEntryHeight(SerializedProperty keyProperty, SerializedProperty valueProperty, int warnings)
        {
            var result =
                EditorGUI.GetPropertyHeight(keyProperty, includeChildren: true) +
                EditorGUIUtility.standardVerticalSpacing +
                EditorGUI.GetPropertyHeight(valueProperty, includeChildren: true);

            if (warnings > 0)
            {
                result += warnings * EditorGUIUtility.standardVerticalSpacing + EditorGUIUtility.standardVerticalSpacing + warnings * EditorGUIUtility.singleLineHeight;
            }

            return result;
        }

        private static void OnDrawEmpty(Rect rect)
        {
            EditorGUI.LabelField(rect, dictionaryIsEmptyLabel);
        }

        private static void OnDrawListElement(SerializedProperty property, Rect rect, int index, bool active, bool focused, List<Warning> warnings)
        {
            if (Event.current.type == EventType.Layout)
            {
                return;
            }

            var keys = property.FindPropertyRelative("_keys");
            var values = property.FindPropertyRelative("_values");

            if (keys.arraySize <= index)
            {
                return;
            }

            var key = keys.GetArrayElementAtIndex(index);
            var value = values.GetArrayElementAtIndex(index);

            if (warnings.Count > 0)
            {
                var warningsRect = new Rect(rect);
                warningsRect.height =
                    warnings.Count * EditorGUIUtility.singleLineHeight +
                    warnings.Count * EditorGUIUtility.standardVerticalSpacing +
                    EditorGUIUtility.standardVerticalSpacing;

                for (int i = 0; i < warnings.Count; ++i)
                {
                    var warnRect = new Rect(warningsRect);
                    warnRect.y += i * EditorGUIUtility.singleLineHeight + (i + 1) * EditorGUIUtility.standardVerticalSpacing;
                    warnRect.height = EditorGUIUtility.singleLineHeight;

                    EditorGUI.HelpBox(warnRect, warnings[i].Text, warnings[i].Type);
                }

                rect.y += warningsRect.height;
            }

            var keyRect = new Rect(rect);
            keyRect.height = EditorGUI.GetPropertyHeight(key);
            keyRect.y += EditorGUIUtility.standardVerticalSpacing;

            var valueRect = new Rect(rect);
            var offset = keyRect.height + EditorGUIUtility.standardVerticalSpacing * 2;
            valueRect.y += offset;
            valueRect.height -= offset;

            if (key.propertyType == SerializedPropertyType.Generic)
            {
                EditorGUI.PropertyField(keyRect, key, label: genericKeyLabel, includeChildren: true);
            }
            else
            {
                EditorGUI.PropertyField(keyRect, key, label: GUIContent.none);
            }

            EditorGUI.PropertyField(valueRect, value, label: new GUIContent("Value", value.displayName), includeChildren: true);
        }

        private static void OnReorder(SerializedProperty property, int oldIndex, int newIndex)
        {
            // key is used as list item, so its already reordered when this callback executed
            // property.FindPropertyRelative("_keys").MoveArrayElement(oldIndex, newIndex);

            var values = property.FindPropertyRelative("_values");
            values.MoveArrayElement(oldIndex, newIndex);

            var oldValue = values.GetArrayElementAtIndex(newIndex);
            var oldExpanded = oldValue.isExpanded;
            var newValue = values.GetArrayElementAtIndex(oldIndex);
            oldValue.isExpanded = newValue.isExpanded;
            newValue.isExpanded = oldExpanded;
        }

        private static void OnAdded(SerializedProperty property)
        {
            var keys = property.FindPropertyRelative("_keys");
            var values = property.FindPropertyRelative("_values");
            var count = keys.arraySize;

            keys.InsertArrayElementAtIndex(count);
            values.InsertArrayElementAtIndex(count);

            SetPropertyDefault(keys.GetArrayElementAtIndex(count), keys);
            SetPropertyDefault(values.GetArrayElementAtIndex(count), values);
        }

        private static void OnRemoved(SerializedProperty property, ReorderableList list)
        {
            var keys = property.FindPropertyRelative("_keys");
            var values = property.FindPropertyRelative("_values");

            foreach (var index in list.selectedIndices.OrderByDescending(i => i))
            {
                keys.DeleteArrayElementAtIndex(index);
                values.DeleteArrayElementAtIndex(index);
            }
        }

        private static void SetPropertyDefault(SerializedProperty property, SerializedProperty parent, bool skipGeneric = false)
        {
            if (property == null)
            {
                throw new System.ArgumentNullException("prop");
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    property.intValue = default;
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = default;
                    break;
                case SerializedPropertyType.Float:
                    property.floatValue = default;
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = string.Empty;
                    break;
                case SerializedPropertyType.Color:
                    property.colorValue = Color.black;
                    break;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = null;
                    break;
                case SerializedPropertyType.LayerMask:
                    property.intValue = -1;
                    break;
                case SerializedPropertyType.Enum:
                    int index = 0;

                    if (parent != null)
                    {
                        var numbersUsed = new List<int>();
                        for (int i = 0; i < parent.arraySize; i++)
                        {
                            numbersUsed.Add(parent.GetArrayElementAtIndex(i).enumValueIndex);
                        }

                        while (true)
                        {
                            if (!numbersUsed.Contains(index))
                            {
                                break;
                            }

                            index++;
                        }

                        if (index >= property.enumNames.Length)
                        {
                            index = 0;
                        }
                    }

                    property.enumValueIndex = index;
                    break;
                case SerializedPropertyType.Vector2:
                    property.vector2Value = Vector2.zero;
                    break;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = Vector3.zero;
                    break;
                case SerializedPropertyType.Vector4:
                    property.vector4Value = Vector4.zero;
                    break;
                case SerializedPropertyType.Rect:
                    property.rectValue = Rect.zero;
                    break;
                case SerializedPropertyType.ArraySize:
                    property.arraySize = 0;
                    break;
                case SerializedPropertyType.Character:
                    property.intValue = 0;
                    break;
                case SerializedPropertyType.AnimationCurve:
                    property.animationCurveValue = null;
                    break;
                case SerializedPropertyType.Bounds:
                    property.boundsValue = default(Bounds);
                    break;
                case SerializedPropertyType.Gradient:
                    SetGradientValue(property, new Gradient());
                    break;
                case SerializedPropertyType.Generic:
                    if (!skipGeneric)
                    {
                        var t = property.GetEnumerator();
                        while (t.MoveNext())
                        {
                            var val = t.Current;
                            SetPropertyDefault((val as SerializedProperty), null, skipGeneric: true);
                        }
                    }
                    break;
                case SerializedPropertyType.Quaternion:
                    property.quaternionValue = Quaternion.identity;
                    break;
                default:
                    Debug.Log("Type not implemented: " + property.propertyType);
                    break;
            }
        }

        private static void SetGradientValue(SerializedProperty prop, Gradient gradient)
        {
            var propertyInfo = typeof(SerializedProperty).GetProperty("gradientValue", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (propertyInfo == null)
            {
                return;
            }

            propertyInfo.SetValue(prop, gradient, null);
        }
    }
}
