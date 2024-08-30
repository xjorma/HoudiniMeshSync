using UnityEngine;
using UnityEditor;

namespace LookingGlass.Editor {
    [CustomPropertyDrawer(typeof(RenderStack))]
    public class RenderStackDrawer : PropertyDrawer {
        private const bool AutoEnableNewRenderSteps = true;
        private GUIContent quiltMixLabel;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            SerializedProperty steps = property.FindPropertyRelative("steps");
            int prevCount = steps.arraySize;

            Rect stepsRect = position;
            stepsRect.height = EditorGUI.GetPropertyHeight(steps);

            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(stepsRect, steps, label, true);
            if (EditorGUI.EndChangeCheck()) {
                if (AutoEnableNewRenderSteps) {
                    for (int i = prevCount; i < steps.arraySize; i++)
                        steps.GetArrayElementAtIndex(i).FindPropertyRelative("isEnabled").boolValue = true;
                }
            }

            RenderTexture quiltMix = property.GetValue<RenderStack>().QuiltMix;
            Rect quiltMixRect = stepsRect;
            quiltMixRect.y = stepsRect.yMax + EditorGUIUtility.standardVerticalSpacing;
            quiltMixRect.height = EditorGUIUtility.singleLineHeight;

            if (quiltMixLabel == null)
                quiltMixLabel = new GUIContent("Quilt Mix", "The final render result that combines " +
                    "all of the render steps in the stack, in the order shown in the inspector.");
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.ObjectField(quiltMixRect, quiltMixLabel, quiltMix, typeof(RenderTexture), true);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            SerializedProperty steps = property.FindPropertyRelative("steps");
            return EditorGUI.GetPropertyHeight(steps) +
                (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
        }
    }
}