using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Unity.Cinemachine;

public class AnimationTimelineController : MonoBehaviour
{
    [System.Serializable]
    public class AnimClipInfo
    {
        public string clipName;
        public int frameCount;

        [HideInInspector] public AnimationClip clip;
        [HideInInspector] public AnimationClipPlayable playable;
        [HideInInspector] public int startFrame;
        [HideInInspector] public int endFrame;
    }

    [System.Serializable]
    public class CameraKeyframe
    {
        public int frame;                    // 이 프레임에서
        public Transform cameraTransform;    // 이 위치/회전으로
        [Tooltip("Optional: Target to look at")]
        public Transform lookAtTarget;       // 이 타겟을 바라봄 (선택)

        public string MakeJson()
        {
            Vector3 pos = cameraTransform.position;
            return $"{{\"frame\":{frame},\"position\":{{\"x\":{pos.x},\"y\":{pos.y},\"z\":{pos.z}}}}}";
        }
    }

    [Header("Setup")]
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private Slider timelineSlider;
    [SerializeField] private AnimClipInfo[] animationClips;

    [Header("Camera")]
    [SerializeField] private CinemachineCamera virtualCamera;  // 단일 카메라 사용
    [SerializeField] private CameraKeyframe[] cameraKeyframes; // 카메라 키프레임들
    [SerializeField] private bool smoothCameraMovement = true;

    [Header("Settings")]
    [SerializeField] private int fps = 30;
    [SerializeField] private int blendFrames = 10;

    private PlayableGraph playableGraph;
    private AnimationMixerPlayable mixer;
    private RuntimeAnimatorController originalController;
    private int totalFrames;

    [Header("Debug Visualization")] // 👈 새로 추가
    [SerializeField] private bool showCameraPath = true;
    [SerializeField] private Color pathColor = Color.cyan;
    [SerializeField] private Color keyframeColor = Color.yellow;
    [SerializeField] private float keyframeSphereSize = 0.3f;
    [SerializeField] private int pathResolution = 50; // 경로 세밀도

    void Start()
    {
        Initialize();
    }

    void Initialize()
    {
        originalController = targetAnimator.runtimeAnimatorController;
        ExtractClips();

        targetAnimator.runtimeAnimatorController = null;
        targetAnimator.enabled = true;

        CreatePlayableGraph();
        CalculateTimeline();
        SetupSlider();

        // 카메라 키프레임 정렬
        SortCameraKeyframes();

        SetFrame(0);

        Debug.Log($"✅ Animation Timeline initialized: {totalFrames} frames, {cameraKeyframes.Length} camera keyframes");
    }

    void ExtractClips()
    {
        var clips = originalController.animationClips;

        foreach (var animInfo in animationClips)
        {
            foreach (var clip in clips)
            {
                if (clip.name.ToLower().Contains(animInfo.clipName.ToLower()) ||
                    animInfo.clipName.ToLower().Contains(clip.name.ToLower()))
                {
                    animInfo.clip = clip;

                    if (animInfo.frameCount == 0)
                    {
                        animInfo.frameCount = Mathf.CeilToInt(clip.length * fps);
                    }

                    Debug.Log($"Found clip: {clip.name} → {animInfo.frameCount} frames");
                    break;
                }
            }

            if (animInfo.clip == null)
            {
                Debug.LogError($"❌ Clip '{animInfo.clipName}' not found in Animator!");
            }
        }
    }

    void CreatePlayableGraph()
    {
        playableGraph = PlayableGraph.Create("AnimationTimeline");
        playableGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

        mixer = AnimationMixerPlayable.Create(playableGraph, animationClips.Length);

        for (int i = 0; i < animationClips.Length; i++)
        {
            var animInfo = animationClips[i];

            if (animInfo.clip != null)
            {
                var playable = AnimationClipPlayable.Create(playableGraph, animInfo.clip);
                animInfo.playable = playable;

                playableGraph.Connect(playable, 0, mixer, i);
                mixer.SetInputWeight(i, 0f);
            }
        }

        var output = AnimationPlayableOutput.Create(playableGraph, "Animation", targetAnimator);
        output.SetSourcePlayable(mixer);

        playableGraph.Play();
    }

    void CalculateTimeline()
    {
        int accumulatedFrames = 0;

        foreach (var animInfo in animationClips)
        {
            animInfo.startFrame = accumulatedFrames;
            animInfo.endFrame = accumulatedFrames + animInfo.frameCount - 1;
            accumulatedFrames += animInfo.frameCount;

            Debug.Log($"  [{animInfo.clipName}] Frames: {animInfo.startFrame} ~ {animInfo.endFrame}");
        }

        totalFrames = accumulatedFrames;
    }

    void SetupSlider()
    {
        if (timelineSlider != null)
        {
            timelineSlider.minValue = 0;
            timelineSlider.maxValue = totalFrames - 1;
            timelineSlider.wholeNumbers = true;
            timelineSlider.value = 0;

            timelineSlider.onValueChanged.RemoveAllListeners();
            timelineSlider.onValueChanged.AddListener(value => SetFrame((int)value));
        }
    }

    void SortCameraKeyframes()
    {
        if (cameraKeyframes != null && cameraKeyframes.Length > 0)
        {
            System.Array.Sort(cameraKeyframes, (a, b) => a.frame.CompareTo(b.frame));
        }
    }

    public void SetFrame(int frame)
    {
        frame = Mathf.Clamp(frame, 0, totalFrames - 1);

        // 애니메이션 업데이트
        UpdateAnimation(frame);

        // 카메라 업데이트
        UpdateCamera(frame);
    }

    void UpdateAnimation(int frame)
    {
        for (int i = 0; i < animationClips.Length; i++)
        {
            mixer.SetInputWeight(i, 0f);
        }

        for (int i = 0; i < animationClips.Length; i++)
        {
            var animInfo = animationClips[i];

            if (frame >= animInfo.startFrame && frame <= animInfo.endFrame)
            {
                int localFrame = frame - animInfo.startFrame;
                float time = localFrame / (float)fps;
                time = Mathf.Clamp(time, 0f, animInfo.clip.length);

                animInfo.playable.SetTime(time);

                bool isBlendStart = (i > 0 && localFrame < blendFrames);
                bool isBlendEnd = (i < animationClips.Length - 1 &&
                                   localFrame > (animInfo.frameCount - blendFrames));

                if (isBlendStart)
                {
                    float blendRatio = (float)localFrame / blendFrames;
                    mixer.SetInputWeight(i, blendRatio);

                    var prevAnim = animationClips[i - 1];
                    float prevTime = Mathf.Clamp(prevAnim.clip.length - 0.01f, 0f, prevAnim.clip.length);
                    prevAnim.playable.SetTime(prevTime);
                    mixer.SetInputWeight(i - 1, 1f - blendRatio);
                }
                else if (isBlendEnd)
                {
                    int framesFromEnd = animInfo.frameCount - localFrame;
                    float blendRatio = (float)framesFromEnd / blendFrames;
                    mixer.SetInputWeight(i, blendRatio);

                    var nextAnim = animationClips[i + 1];
                    nextAnim.playable.SetTime(0f);
                    mixer.SetInputWeight(i + 1, 1f - blendRatio);
                }
                else
                {
                    mixer.SetInputWeight(i, 1f);
                }

                break;
            }
        }

        playableGraph.Evaluate(0f);
    }

    void UpdateCamera(int frame)
    {
        if (virtualCamera == null || cameraKeyframes == null || cameraKeyframes.Length == 0)
            return;

        // 현재 프레임 기준으로 이전/다음 키프레임 찾기
        CameraKeyframe prevKeyframe = null;
        CameraKeyframe nextKeyframe = null;

        for (int i = 0; i < cameraKeyframes.Length; i++)
        {
            if (cameraKeyframes[i].frame <= frame)
            {
                prevKeyframe = cameraKeyframes[i];
            }

            if (cameraKeyframes[i].frame >= frame && nextKeyframe == null)
            {
                nextKeyframe = cameraKeyframes[i];
            }
        }

        // 정확히 키프레임 위치에 있을 때
        if (prevKeyframe != null && prevKeyframe.frame == frame)
        {
            ApplyCameraKeyframe(prevKeyframe);
            return;
        }

        // 두 키프레임 사이에서 보간
        if (smoothCameraMovement && prevKeyframe != null && nextKeyframe != null &&
            prevKeyframe != nextKeyframe)
        {
            float t = (float)(frame - prevKeyframe.frame) / (nextKeyframe.frame - prevKeyframe.frame);
            InterpolateCameraKeyframes(prevKeyframe, nextKeyframe, t);
        }
        else if (prevKeyframe != null)
        {
            // 마지막 키프레임 이후거나 보간 없음
            ApplyCameraKeyframe(prevKeyframe);
        }
    }

    void ApplyCameraKeyframe(CameraKeyframe keyframe)
    {
        if (keyframe.cameraTransform != null)
        {
            virtualCamera.transform.position = keyframe.cameraTransform.position;

            if (keyframe.lookAtTarget != null)
            {
                virtualCamera.transform.LookAt(keyframe.lookAtTarget);
            }
            else
            {
                virtualCamera.transform.rotation = keyframe.cameraTransform.rotation;
            }
        }
    }

    void InterpolateCameraKeyframes(CameraKeyframe from, CameraKeyframe to, float t)
    {
        if (from.cameraTransform != null && to.cameraTransform != null)
        {
            // 위치 보간
            virtualCamera.transform.position = Vector3.Lerp(
                from.cameraTransform.position,
                to.cameraTransform.position,
                t
            );

            // 회전 보간
            if (from.lookAtTarget != null && to.lookAtTarget != null)
            {
                // LookAt 타겟이 있으면 보간된 위치에서 타겟 바라보기
                Vector3 targetPos = Vector3.Lerp(
                    from.lookAtTarget.position,
                    to.lookAtTarget.position,
                    t
                );
                virtualCamera.transform.LookAt(targetPos);
            }
            else
            {
                virtualCamera.transform.rotation = Quaternion.Slerp(
                    from.cameraTransform.rotation,
                    to.cameraTransform.rotation,
                    t
                );
            }
        }
    }

    public void RestoreAnimator()
    {
        if (playableGraph.IsValid())
        {
            playableGraph.Destroy();
        }

        if (targetAnimator != null && originalController != null)
        {
            targetAnimator.runtimeAnimatorController = originalController;
            targetAnimator.enabled = true;
        }
    }

    void OnDestroy()
    {
        if (playableGraph.IsValid())
        {
            playableGraph.Destroy();
        }
    }

    public int GetTotalFrames() => totalFrames;
    public int GetCurrentFrame() => (int)timelineSlider.value;
    public float GetCurrentTime() => GetCurrentFrame() / (float)fps;


    #region Debug Visualization


    void OnDrawGizmos()
    {
        if (!showCameraPath || cameraKeyframes == null || cameraKeyframes.Length == 0)
            return;

        DrawCameraPath();
    }

    // OnDrawGizmosSelected: 이 GameObject 선택했을 때만 표시
    void OnDrawGizmosSelected()
    {
        if (!showCameraPath || cameraKeyframes == null || cameraKeyframes.Length == 0)
            return;

        DrawKeyframeDetails();
    }

    void DrawCameraPath()
    {
        // 유효한 키프레임만 필터링
        var validKeyframes = System.Array.FindAll(cameraKeyframes,
            k => k.cameraTransform != null);

        if (validKeyframes.Length < 2)
            return;

        Gizmos.color = pathColor;

        if (smoothCameraMovement)
        {
            // 부드러운 경로 그리기 (보간된 경로)
            DrawSmoothPath(validKeyframes);
        }
        else
        {
            // 직선 경로 그리기
            DrawStraightPath(validKeyframes);
        }

        // 키프레임 위치 표시
        DrawKeyframeMarkers(validKeyframes);
    }

    void DrawSmoothPath(CameraKeyframe[] keyframes)
    {
        Vector3 prevPos = keyframes[0].cameraTransform.position;

        for (int i = 0; i < keyframes.Length - 1; i++)
        {
            CameraKeyframe from = keyframes[i];
            CameraKeyframe to = keyframes[i + 1];

            // 두 키프레임 사이를 여러 점으로 보간
            for (int j = 0; j <= pathResolution; j++)
            {
                float t = j / (float)pathResolution;
                Vector3 pos = Vector3.Lerp(
                    from.cameraTransform.position,
                    to.cameraTransform.position,
                    t
                );

                if (j > 0)
                {
                    Gizmos.DrawLine(prevPos, pos);
                }
                prevPos = pos;
            }
        }
    }

    void DrawStraightPath(CameraKeyframe[] keyframes)
    {
        for (int i = 0; i < keyframes.Length - 1; i++)
        {
            Vector3 from = keyframes[i].cameraTransform.position;
            Vector3 to = keyframes[i + 1].cameraTransform.position;
            Gizmos.DrawLine(from, to);
        }
    }

    void DrawKeyframeMarkers(CameraKeyframe[] keyframes)
    {
        Gizmos.color = keyframeColor;

        foreach (var keyframe in keyframes)
        {
            if (keyframe.cameraTransform != null)
            {
                // 키프레임 위치에 구 그리기
                Gizmos.DrawSphere(keyframe.cameraTransform.position, keyframeSphereSize);

                // Look At Target이 있으면 화살표 그리기
                if (keyframe.lookAtTarget != null)
                {
                    Gizmos.color = Color.green;
                    Vector3 direction = (keyframe.lookAtTarget.position - keyframe.cameraTransform.position).normalized;
                    Gizmos.DrawRay(keyframe.cameraTransform.position, direction * 2f);
                    Gizmos.color = keyframeColor;
                }
            }
        }
    }

    void DrawKeyframeDetails()
    {
        if (cameraKeyframes == null)
            return;

        // 각 키프레임에 프레임 번호 표시
        for (int i = 0; i < cameraKeyframes.Length; i++)
        {
            var keyframe = cameraKeyframes[i];
            if (keyframe.cameraTransform != null)
            {
                // 프레임 번호 텍스트
                Vector3 pos = keyframe.cameraTransform.position + Vector3.up * 0.5f;

#if UNITY_EDITOR
                UnityEditor.Handles.color = Color.white;
                UnityEditor.Handles.Label(pos, $"Frame {keyframe.frame}");
#endif

                // 카메라 방향 표시
                Gizmos.color = Color.blue;
                Vector3 forward = keyframe.cameraTransform.forward;
                Gizmos.DrawRay(keyframe.cameraTransform.position, forward * 1.5f);

                // FOV 표시 (원뿔 형태)
                DrawCameraFrustum(keyframe.cameraTransform);
            }
        }
    }

    void DrawCameraFrustum(Transform camTransform)
    {
        if (virtualCamera == null)
            return;

        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);

        float fov = 60f; // 기본값, Cinemachine 설정에 따라 조정
        float distance = 3f;
        float aspect = 16f / 9f;

        Vector3 pos = camTransform.position;
        Vector3 forward = camTransform.forward;
        Vector3 right = camTransform.right;
        Vector3 up = camTransform.up;

        float height = 2f * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * distance;
        float width = height * aspect;

        Vector3 center = pos + forward * distance;
        Vector3 topLeft = center + up * (height / 2f) - right * (width / 2f);
        Vector3 topRight = center + up * (height / 2f) + right * (width / 2f);
        Vector3 bottomLeft = center - up * (height / 2f) - right * (width / 2f);
        Vector3 bottomRight = center - up * (height / 2f) + right * (width / 2f);

        // 원뿔 선 그리기
        Gizmos.DrawLine(pos, topLeft);
        Gizmos.DrawLine(pos, topRight);
        Gizmos.DrawLine(pos, bottomLeft);
        Gizmos.DrawLine(pos, bottomRight);

        // 카메라 프레임
        Gizmos.DrawLine(topLeft, topRight);
        Gizmos.DrawLine(topRight, bottomRight);
        Gizmos.DrawLine(bottomRight, bottomLeft);
        Gizmos.DrawLine(bottomLeft, topLeft);
    }
    #endregion
}