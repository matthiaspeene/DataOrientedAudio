// Assets/Scripts/Audio/Editor/DbGainSliderDrawer.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using DataOrientedAudio.Common;

namespace DataOrientedAudio.Editor
{
    [CustomPropertyDrawer(typeof(DbGainSliderAttribute))]
    public sealed class DbGainSliderDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var attr = (DbGainSliderAttribute)attribute;
            bool showLinear = attr.ShowLinearReadout && property.propertyType == SerializedPropertyType.Float;
            return EditorGUIUtility.singleLineHeight * (showLinear ? 2f : 1f) + 4f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var attr = (DbGainSliderAttribute)attribute;

            if (property.propertyType != SerializedPropertyType.Float)
            {
                EditorGUI.HelpBox(position, $"{property.displayName} must be a float to use [DbGainSlider].", MessageType.Warning);
                return;
            }

            // force label to "Gain (dB)" regardless of field name
            label = new GUIContent("Gain (dB)");

            float lin = Mathf.Clamp(property.floatValue, 0f, 64f);
            float db = DbMath.LinearToDb(Mathf.Max(1e-20f, lin));

            var lineH = EditorGUIUtility.singleLineHeight;
            var r1 = new Rect(position.x, position.y, position.width, lineH);
            var r2 = new Rect(position.x, r1.yMax + 2f, position.width, lineH);

            db = EditorGUI.Slider(r1, label, db, attr.MinDb, attr.MaxDb);
            db = DbMath.RoundToStep(DbMath.ClampDb(db));

            property.floatValue = DbMath.DbToLinear(db);

            if (attr.ShowLinearReadout)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.Slider(r2, new GUIContent("Gain (Linear)"), property.floatValue, 0f, 1f);
                }
            }
        }
    }
}
#endif
