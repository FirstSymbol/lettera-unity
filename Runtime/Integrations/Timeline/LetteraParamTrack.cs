using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace FirstSymbol.Lettera.Timeline
{
    /// <summary>Takes one linked effect parameter along a timeline.</summary>
    [DisplayName("Lettera/Parameter Track")]
    [TrackColor(0.55f, 0.42f, 0.95f)]
    [TrackClipType(typeof(LetteraParamShot))]
    [TrackBindingType(typeof(LetteraParamLink))]
    public class LetteraParamTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount) =>
            ScriptPlayable<LetteraParamMixer>.Create(graph, inputCount);

        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            var link = director.GetGenericBinding(this) as LetteraParamLink;
            if (link != null) driver.AddFromName<LetteraParamLink>(link.gameObject, "m_Value");
            base.GatherProperties(director, driver);
        }
    }
}
