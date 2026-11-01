using System.Collections.Generic;
using System.IO;
using FirstSymbol.Lettera.Editor.Tests;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace FirstSymbol.Lettera.Timeline.Editor.Tests
{
    /// <summary>Texts, directors and temporary scenes for the Timeline tests.</summary>
    public abstract class TimelineFixture : TextFixture
    {
        protected const string TempScene = "Assets/_LetteraTimelineProof.unity";
        protected const string TempTimeline = "Assets/_LetteraTimelineProof.playable";

        readonly List<Object> m_Made = new List<Object>();
        PlayableDirector m_Director;

        protected T Own<T>(T thing) where T : Object
        {
            m_Made.Add(thing);
            return thing;
        }

        [TearDown]
        public void Drop()
        {
            if (m_Director != null && m_Director.playableGraph.IsValid()) m_Director.Stop();
            foreach (Object made in m_Made)
                if (made != null)
                    Object.DestroyImmediate(made);
            m_Made.Clear();
            m_Director = null;
            LetteraMotionHub.Tick(0.0001f, 0.0001f);
            if (File.Exists(TempScene))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(TempScene);
            }
            if (File.Exists(TempTimeline)) AssetDatabase.DeleteAsset(TempTimeline);
        }

        protected LetteraSequencer Sequenced(out LetteraWorldText text)
        {
            text = Make("");
            text.Styles.Clear();
            text.Styles.Add(LetteraStyle.ForTag(new WaveMotion(), "go"));
            text.Text = "<go>wave</go>";
            text.ForceRebuild();
            var sequencer = text.gameObject.AddComponent<LetteraSequencer>();
            sequencer.Cues.Add(new LetteraCue
            {
                Target = new LetteraParamTarget { Style = 0, Parameter = "Amplitude" },
                From = LetteraValue.Of(0),
                To = LetteraValue.Of(1),
                UnitSeconds = 2,
            });
            return sequencer;
        }

        protected LetteraParamLink Linked(out LetteraWorldText text)
        {
            text = Make("");
            text.Styles.Clear();
            text.Styles.Add(LetteraStyle.ForTag(new WaveMotion(), "go"));
            text.Text = "<go>wave</go>";
            text.ForceRebuild();
            var link = text.gameObject.AddComponent<LetteraParamLink>();
            link.Target.Style = 0;
            link.Target.Parameter = "Amplitude";
            return link;
        }

        protected static float Held(LetteraWorldText text)
        {
            LetteraMotionHub.Tick(0.0001f, 0.0001f);
            text.ForceRebuild();
            return text.Engine.MotionValueForTests(0, 0);
        }

        protected PlayableDirector Director(TimelineAsset timeline)
        {
            var go = Own(new GameObject("Director"));
            m_Director = go.AddComponent<PlayableDirector>();
            m_Director.timeUpdateMode = DirectorUpdateMode.Manual;
            m_Director.extrapolationMode = DirectorWrapMode.Hold;
            m_Director.playOnAwake = false;
            m_Director.playableAsset = timeline;
            return m_Director;
        }

        protected TimelineAsset Timeline() => Own(ScriptableObject.CreateInstance<TimelineAsset>());

        protected static void At(PlayableDirector director, double time)
        {
            director.time = time;
            director.Evaluate();
        }

        /// <summary>A saved scene holding these, so that isDirty means something.</summary>
        protected Scene CleanScene(params GameObject[] roots)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (GameObject root in roots) SceneManager.MoveGameObjectToScene(root, scene);
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, TempScene));
            Assert.IsFalse(scene.isDirty);
            return scene;
        }

        /// <summary>What Timeline's preview does with the properties a track names: AnimationMode gives them back on leaving.</summary>
        protected sealed class PreviewCollector : IPropertyCollector
        {
            public readonly List<string> Names = new List<string>();
            public void PushActiveGameObject(GameObject gameObject) { }
            public void PopActiveGameObject() { }
            public void AddFromClip(AnimationClip clip) { }
            public void AddFromClips(IEnumerable<AnimationClip> clips) { }
            public void AddFromName<T>(string name) where T : Component { }
            public void AddFromName(string name) { }
            public void AddFromClip(GameObject obj, AnimationClip clip) { }
            public void AddFromClips(GameObject obj, IEnumerable<AnimationClip> clips) { }
            public void AddFromName<T>(GameObject obj, string name) where T : Component => AddFromName(obj.GetComponent<T>(), name);
            public void AddFromName(GameObject obj, string name) { }

            public void AddFromName(Component component, string name)
            {
                Names.Add(name);
                SerializedProperty property = new SerializedObject(component).FindProperty(name);
                Assert.IsNotNull(property, name);
                string value = property.propertyType == SerializedPropertyType.Float ? property.floatValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : property.intValue.ToString();
                AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("", component.GetType(), name), new PropertyModification { target = component, propertyPath = name, value = value }, false);
            }

            public void AddFromComponent(GameObject obj, Component component) { }
            public void AddObjectProperties(Object obj, AnimationClip clip) { }
        }
    }
}
