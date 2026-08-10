using UnityEngine;
#nullable enable
namespace Assets.Scripts.Types
{
    public class ConnSlideInfo
    {
        /// <summary>
        /// Total duration of the Slide Group
        /// </summary>
        public float TotalLength { get; set; }
        /// <summary>
        /// Total length of the Slide Group
        /// </summary>
        public float TotalSlideLen { get; set; }
        /// <summary>
        /// Whether this Slide is at the start of the Group
        /// </summary>
        public bool IsGroupPartHead 
        {
            get => IsConnSlide && _isGroupPartHead;
            set => _isGroupPartHead = value;
        }
        /// <summary>
        /// Whether this Slide belongs to a Group
        /// </summary>
        public bool IsGroupPart { get; set; }
        /// <summary>
        /// Whether this Slide is at the end of the Group
        /// </summary>
        public bool IsGroupPartEnd 
        {
            get => IsConnSlide && _isGroupPartEnd;
            set => _isGroupPartEnd = value;
        }
        /// <summary>
        /// Gets the GameObject for the preceding Slide
        /// </summary>
        public GameObject? Parent { get; set; } = null;
        /// <summary>
        /// null
        /// </summary>
        public bool DestroyAfterJudge
        {
            get => IsGroupPartEnd;
        }
        /// <summary>
        /// Whether the current Slide is a Connection Slide
        /// </summary>
        public bool IsConnSlide { get => IsGroupPart; }
        /// <summary>
        /// Whether the preceding Slide has finished
        /// </summary>
        public bool ParentFinished
        {
            get
            {
                if (Parent == null)
                    return true;
                else
                    return Parent.GetComponent<SlideDrop>().isFinished;
            }
        }
        /// <summary>
        /// Whether the preceding Slide is pending completion
        /// </summary>
        public bool ParentPendingFinish
        {
            get
            {
                if (Parent == null)
                    return false;
                else
                    return Parent.GetComponent<SlideDrop>().isPendingFinish;
            }
        }
        bool _isGroupPartEnd = false;
        bool _isGroupPartHead = false;

    }
}
