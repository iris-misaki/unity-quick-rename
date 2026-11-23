using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LevelNameTranslator
{
    internal static class AnimationPathFixer
    {
        internal sealed class Plan
        {
            internal readonly List<ClipEdit> Edits = new List<ClipEdit>();
            internal readonly List<string> Issues = new List<string>();
        }

        internal sealed class ClipEdit
        {
            internal AnimationClip Clip;
            internal readonly List<CurveEdit> Curves = new List<CurveEdit>();
        }

        internal sealed class CurveEdit
        {
            internal EditorCurveBinding Old, New;
            internal AnimationCurve Float;
            internal ObjectReferenceKeyframe[] Object;
            internal bool IsObject;
        }

        // Controller ownership is discovered from actual object references, never by scanning
        // for matching strings across unrelated animation files.
        private static HashSet<AnimationClip> ClipsAt(Transform root)
        {
            var clips = new HashSet<AnimationClip>();
            void Add(RuntimeAnimatorController controller)
            {
                if (controller == null) return;
                foreach (var clip in controller.animationClips) if (clip != null) clips.Add(clip);
            }
            var animator = root.GetComponent<Animator>();
            if (animator != null) Add(animator.runtimeAnimatorController);
            foreach (var component in root.GetComponents<MonoBehaviour>())
            {
                if (component == null || component.GetType().FullName != "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor") continue;
                using (var serialized = new SerializedObject(component))
                {
                    foreach (var field in new[] { "baseAnimationLayers", "specialAnimationLayers" })
                    {
                        var layers = serialized.FindProperty(field);
                        if (layers == null || !layers.isArray) continue;
                        for (int i = 0; i < layers.arraySize; i++)
                        {
                            var layer = layers.GetArrayElementAtIndex(i);
                            if (layer.FindPropertyRelative("isDefault")?.boolValue == true) continue;
                            Add(layer.FindPropertyRelative("animatorController")?.objectReferenceValue as RuntimeAnimatorController);
                        }
                    }
                }
            }
            return clips;
        }

        internal static Plan Build(Dictionary<Transform, string> names)
        {
            var plan = new Plan();
            var roots = new HashSet<Transform>();
            foreach (var item in names)
                for (var parent = item.Key.parent; parent != null; parent = parent.parent)
                    roots.Add(parent);
            var usage = new Dictionary<AnimationClip, List<Transform>>();
            foreach (var root in roots)
                foreach (var clip in ClipsAt(root))
                {
                    if (!usage.TryGetValue(clip, out var list)) usage[clip] = list = new List<Transform>();
                    list.Add(root);
                }

            // Refuse shared clips when another loaded hierarchy uses them with different bindings.
            foreach (var animator in Resources.FindObjectsOfTypeAll<Animator>())
            {
                if (animator == null || roots.Contains(animator.transform) || EditorUtility.IsPersistent(animator) || !animator.gameObject.scene.IsValid()) continue;
                foreach (var clip in ClipsAt(animator.transform))
                    if (usage.TryGetValue(clip, out var owners)) owners.Add(animator.transform);
            }
            foreach (var item in usage)
            {
                var edit = new ClipEdit { Clip = item.Key };
                var path = AssetDatabase.GetAssetPath(item.Key);
                void Collect(EditorCurveBinding[] bindings, bool isObject)
                {
                    var destinations = new HashSet<(string, Type, string)>();
                    foreach (var binding in bindings)
                    {
                        string next = null;
                        foreach (var root in item.Value)
                        {
                            string candidate = Resolve(root, binding.path, names, out bool ambiguous);
                            if (ambiguous)
                            {
                                plan.Issues.Add(path + "：路径存在重名节点：" + binding.path);
                                break;
                            }
                            if (next != null && candidate != next)
                            {
                                plan.Issues.Add(path + "：多个动画根共用此剪辑，但新路径不一致：" + binding.path);
                                break;
                            }
                            next = candidate;
                        }
                        next = next ?? binding.path;
                        if (!destinations.Add((next, binding.type, binding.propertyName)))
                            plan.Issues.Add(path + "：改名会让曲线绑定冲突：" + next);
                        if (next == binding.path) continue;
                        var updated = binding;
                        updated.path = next;
                        edit.Curves.Add(new CurveEdit { Old = binding, New = updated, IsObject = isObject,
                            Float = isObject ? null : AnimationUtility.GetEditorCurve(item.Key, binding),
                            Object = isObject ? AnimationUtility.GetObjectReferenceCurve(item.Key, binding) : null });
                    }
                }
                Collect(AnimationUtility.GetCurveBindings(item.Key), false);
                Collect(AnimationUtility.GetObjectReferenceCurveBindings(item.Key), true);
                if (edit.Curves.Count == 0) continue;
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) || !AssetDatabase.IsMainAsset(item.Key) || !AssetDatabase.IsOpenForEdit(item.Key))
                    plan.Issues.Add(path + "：不是可编辑的独立 .anim 文件。");
                else if (edit.Curves.Exists(c => c.IsObject ? c.Object == null : c.Float == null))
                    plan.Issues.Add(path + "：无法读取原曲线。");
                else plan.Edits.Add(edit);
            }
            return plan;
        }

        private static string Resolve(Transform root, string path, Dictionary<Transform, string> names, out bool ambiguous)
        {
            ambiguous = false;
            if (string.IsNullOrEmpty(path)) return path;
            var parts = path.Split('/');
            var current = root;
            for (int i = 0; i < parts.Length; i++)
            {
                Transform hit = null;
                for (int c = 0; c < current.childCount; c++)
                {
                    var child = current.GetChild(c);
                    if (child.name != parts[i]) continue;
                    if (hit != null) { ambiguous = true; return path; }
                    hit = child;
                }
                if (hit == null) return path;
                if (names.TryGetValue(hit, out var name)) parts[i] = name;
                current = hit;
            }
            return string.Join("/", parts);
        }

        internal static void Apply(ClipEdit edit)
        {
            var clip = edit.Clip;
            Undo.RegisterCompleteObjectUndo(clip, "层级名翻译");
            foreach (var c in edit.Curves)
                if (c.IsObject) AnimationUtility.SetObjectReferenceCurve(clip, c.Old, null);
                else AnimationUtility.SetEditorCurve(clip, c.Old, null);
            foreach (var c in edit.Curves)
                if (c.IsObject) AnimationUtility.SetObjectReferenceCurve(clip, c.New, c.Object);
                else AnimationUtility.SetEditorCurve(clip, c.New, c.Float);
            foreach (var c in edit.Curves)
                if (c.IsObject ? AnimationUtility.GetObjectReferenceCurve(clip, c.New) == null : AnimationUtility.GetEditorCurve(clip, c.New) == null)
                    throw new InvalidOperationException("Unity 未能写入动画曲线：" + clip.name + " / " + c.New.path);
            EditorUtility.SetDirty(clip);
        }
    }
}
