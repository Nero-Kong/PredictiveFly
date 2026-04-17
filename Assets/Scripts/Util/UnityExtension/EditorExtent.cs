using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityExtension
{
    public static class EditorExtent
    {
        public static bool FoldTitle(bool foldout, GUIContent title, FontStyle fontStyle = FontStyle.Normal, int fontSize = 12)
        {
#if UNITY_EDITOR
            GUIStyle style = new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = fontStyle,
                fontSize = fontSize
            };

            return EditorGUILayout.Foldout(foldout, title, style);
#else
            return foldout;
#endif
        }

        public static Quaternion QuaternionField(GUIContent label, Quaternion quaternion, bool asEuler, params GUILayoutOption[] options)
        {
#if UNITY_EDITOR
            if (asEuler)
                return Quaternion.Euler(EditorGUILayout.Vector3Field(label, quaternion.eulerAngles, options));
            else
                return EditorGUILayout.Vector4Field(label, quaternion.ToVector4(), options).ToQuaternion();
#else
            return quaternion;
#endif
        }

        public static void FlagField<GenericEnum>(GenericEnum flags, bool editable) where GenericEnum : struct, IConvertible
        {
            Type enumType = typeof(GenericEnum);
            if (!enumType.IsEnum)
                throw new ArgumentException("Parameters is not an enum type");

#if UNITY_EDITOR
            EditorGUILayout.LabelField("Flags: ");

            int flags_value = (int)(object)flags; //! Boxing-Unboxing...
            string[] names = Enum.GetNames(typeof(GenericEnum)) as string[];
            int[] values = Enum.GetValues(typeof(GenericEnum)) as int[];

            EditorGUI.BeginDisabledGroup(!editable);
            GUILayout.BeginHorizontal();
            for (int f = 1; f < values.Length; f++)
            {
                if (values[f] % 2 == 0 || values[f] == 1)
                {
                    EditorGUILayout.LabelField(new GUIContent(names[f]), GUILayout.Width(110));
                    EditorGUILayout.Toggle((flags_value & values[f]) == values[f], GUILayout.Width(10));
                    GUILayout.Space(20);
                }
            }
            GUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
#endif
        }

        public static void FlagField<GenericEnum>(Rect position, GenericEnum flags, bool editable) where GenericEnum : struct, IConvertible
        {
        }
    }
}