using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

namespace NetworkExperience.Demo.PhysicDemo1.Editor
{
    [InitializeOnLoad]
    public static class OmniManCharacterSetup
    {
        private const string Root = "Assets/Demo/PhysicDemo1/OmniManCharacter";
        private const string BaseModelPath = Root + "/全能侠_带骨.fbx";
        private const string IdleAnimationPath = Root + "/idel.fbx";
        private const string RunAnimationPath = Root + "/run.fbx";
        private const string PunchAnimationPath = Root + "/punch.fbx";
        private const string HitAnimationPath = Root + "/受击.fbx";

        private const string TextureDirectory = Root + "/Textures";
        private const string MaterialDirectory = Root + "/Materials";
        private const string AnimationDirectory = Root + "/Animations";
        private const string PrefabDirectory = Root + "/Prefabs";

        private const string PackedMapPath =
            TextureDirectory + "/OmniMan_MetallicSmoothness.png";
        private const string MaterialPath = MaterialDirectory + "/OmniMan.mat";
        private const string LegacyMaterialPath = MaterialDirectory + "/OmniMan_URP.mat";
        private const string ControllerPath = AnimationDirectory + "/OmniMan.controller";
        private const string ObsoleteIdleClipPath =
            AnimationDirectory + "/OmniMan_Idle.anim";
        private const string VisualPrefabPath = PrefabDirectory + "/OmniMan.prefab";
        private const string PlayablePrefabPath =
            PrefabDirectory + "/OmniManThirdPerson.prefab";

        static OmniManCharacterSetup()
        {
            EditorApplication.delayCall += RunInitialSetupWhenReady;
        }

        [MenuItem("Tools/Omni Man/Rebuild Character Resources")]
        public static void RebuildCharacterResources()
        {
            Setup(true);
        }

        [MenuItem("Tools/Omni Man/Validate Generated Resources")]
        public static void ValidateGeneratedResources()
        {
            GameObject visualPrefab = RequireAsset<GameObject>(VisualPrefabPath);
            GameObject playablePrefab = RequireAsset<GameObject>(PlayablePrefabPath);
            RequireAsset<Material>(MaterialPath);
            AnimatorController controller = RequireAsset<AnimatorController>(ControllerPath);

            AnimationClip idle = RequireAnimationClip(IdleAnimationPath, "Idle");
            AnimationClip run = RequireAnimationClip(RunAnimationPath, "Run");
            AnimationClip punch = RequireAnimationClip(PunchAnimationPath, "Punch");
            AnimationClip hit = RequireAnimationClip(HitAnimationPath, "Hit");

            ValidateCharacterPrefab(visualPrefab, false);
            ValidateCharacterPrefab(playablePrefab, true);

            string[] requiredParameters = { "Speed", "Grounded", "Attack", "Hit" };
            foreach (string parameterName in requiredParameters)
            {
                if (!controller.parameters.Any(parameter => parameter.name == parameterName))
                    throw new InvalidOperationException(
                        "Animator Controller 缺少参数：" + parameterName);
            }

            BlendTree locomotion = controller.layers[0].stateMachine.states
                .Select(child => child.state.motion)
                .OfType<BlendTree>()
                .FirstOrDefault(tree => tree.name == "Locomotion");
            if (locomotion == null ||
                !locomotion.children.Any(child => child.motion == idle) ||
                !locomotion.children.Any(child => child.motion == run))
            {
                throw new InvalidOperationException(
                    "Locomotion Blend Tree 没有正确引用 Idle/Run。");
            }

            Texture2D baseColor =
                RequireAsset<Texture2D>(RequireTexturePath("texture_pbr_20250901.png"));
            Texture2D packedMap = RequireAsset<Texture2D>(PackedMapPath);
            if (baseColor.width != packedMap.width || baseColor.height != packedMap.height)
                throw new InvalidOperationException("材质贴图尺寸不一致。");

            Debug.Log(
                $"[OmniMan Validation] PASS | Avatar=Humanoid, " +
                $"Idle={idle.length:F3}s, Run={run.length:F3}s, " +
                $"Punch={punch.length:F3}s, Hit={hit.length:F3}s, " +
                $"Textures={baseColor.width}x{baseColor.height}, " +
                "ThirdPerson=CharacterController/InputSystem/OrbitCamera");
        }

        [MenuItem("Tools/Omni Man/Select Playable Prefab")]
        public static void SelectPlayablePrefab()
        {
            Selection.activeObject =
                AssetDatabase.LoadAssetAtPath<GameObject>(PlayablePrefabPath);
        }

        private static void RunInitialSetupWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunInitialSetupWhenReady;
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            GameObject playable =
                AssetDatabase.LoadAssetAtPath<GameObject>(PlayablePrefabPath);
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            bool controllerNeedsUpgrade =
                controller == null ||
                !controller.parameters.Any(parameter => parameter.name == "Speed") ||
                !controller.parameters.Any(parameter => parameter.name == "Attack") ||
                !controller.parameters.Any(parameter => parameter.name == "Hit");
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            bool materialNeedsUpgrade =
                material == null || !IsMaterialShaderCompatible(material);

            if (playable == null || controllerNeedsUpgrade || materialNeedsUpgrade)
                Setup(controllerNeedsUpgrade || materialNeedsUpgrade);
            else
                ValidateGeneratedResources();
        }

        private static void Setup(bool rebuildGeneratedAssets)
        {
            try
            {
                EnsureFolder(TextureDirectory);
                EnsureFolder(MaterialDirectory);
                EnsureFolder(AnimationDirectory);
                EnsureFolder(PrefabDirectory);

                ConfigureBaseModel();
                ExtractEmbeddedTextures();

                Avatar avatar = FindAvatar(BaseModelPath);
                bool useHumanoid = avatar != null && avatar.isValid && avatar.isHuman;
                if (!useHumanoid)
                {
                    Debug.LogWarning(
                        "[OmniMan Setup] Humanoid 自动映射无效，已回退为 Generic Avatar。");
                    avatar = ConfigureBaseModelAsGeneric();
                }

                ConfigureAnimationModel(
                    IdleAnimationPath, "Idle", true, avatar, useHumanoid);
                ConfigureAnimationModel(
                    RunAnimationPath, "Run", true, avatar, useHumanoid);
                ConfigureAnimationModel(
                    PunchAnimationPath, "Punch", false, avatar, useHumanoid);
                ConfigureAnimationModel(
                    HitAnimationPath, "Hit", false, avatar, useHumanoid);

                Material material = CreateOrUpdateMaterial();
                AnimationClip idle = RequireAnimationClip(IdleAnimationPath, "Idle");
                AnimationClip run = RequireAnimationClip(RunAnimationPath, "Run");
                AnimationClip punch = RequireAnimationClip(PunchAnimationPath, "Punch");
                AnimationClip hit = RequireAnimationClip(HitAnimationPath, "Hit");

                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ObsoleteIdleClipPath) != null)
                    AssetDatabase.DeleteAsset(ObsoleteIdleClipPath);

                AnimatorController controller = CreateController(
                    idle, run, punch, hit, rebuildGeneratedAssets);

                CreateCharacterPrefab(
                    VisualPrefabPath,
                    "OmniMan",
                    avatar,
                    controller,
                    material,
                    false,
                    rebuildGeneratedAssets);
                CreateCharacterPrefab(
                    PlayablePrefabPath,
                    "OmniManThirdPerson",
                    avatar,
                    controller,
                    material,
                    true,
                    rebuildGeneratedAssets);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(
                    "[OmniMan Setup] 完成：Idle/Run/Punch/Hit 状态机、" +
                    "第三人称控制和可玩 Prefab 已生成。");
                ValidateGeneratedResources();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                    throw;
            }
        }

        private static void ConfigureBaseModel()
        {
            ModelImporter importer = GetModelImporter(BaseModelPath);
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importBlendShapes = true;
            importer.materialImportMode =
                ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.SaveAndReimport();
        }

        private static Avatar ConfigureBaseModelAsGeneric()
        {
            ModelImporter importer = GetModelImporter(BaseModelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
            return FindAvatar(BaseModelPath);
        }

        private static void ConfigureAnimationModel(
            string assetPath,
            string clipName,
            bool loop,
            Avatar sourceAvatar,
            bool useHumanoid)
        {
            ModelImporter importer = GetModelImporter(assetPath);
            importer.importAnimation = true;
            importer.importBlendShapes = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationType = useHumanoid
                ? ModelImporterAnimationType.Human
                : ModelImporterAnimationType.Generic;

            if (sourceAvatar != null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = sourceAvatar;
            }
            else
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                throw new InvalidOperationException(assetPath + " 中没有可导入的动画 Take。");

            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.name = clipName;
                clip.loopTime = loop;
                clip.loopPose = loop;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static AnimatorController CreateController(
            AnimationClip idle,
            AnimationClip run,
            AnimationClip punch,
            AnimationClip hit,
            bool rebuild)
        {
            AnimatorController existing =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (existing != null && !rebuild)
                return existing;

            if (existing != null)
                AssetDatabase.DeleteAsset(ControllerPath);

            AnimatorController controller =
                AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

            AnimatorState locomotionState =
                controller.CreateBlendTreeInController("Locomotion", out BlendTree locomotion);
            locomotion.blendType = BlendTreeType.Simple1D;
            locomotion.blendParameter = "Speed";
            locomotion.useAutomaticThresholds = false;
            locomotion.AddChild(idle, 0f);
            locomotion.AddChild(run, 0.25f);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            machine.name = "Omni Man";
            machine.defaultState = locomotionState;

            AnimatorState punchState =
                machine.AddState("Punch", new Vector3(520f, -40f));
            punchState.motion = punch;

            AnimatorState hitState =
                machine.AddState("Hit", new Vector3(520f, 120f));
            hitState.motion = hit;

            AddTriggeredTransition(machine, punchState, "Attack", 0.08f);
            AddTriggeredTransition(machine, hitState, "Hit", 0.05f);
            AddReturnTransition(punchState, locomotionState, 0.1f);
            AddReturnTransition(hitState, locomotionState, 0.08f);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void AddTriggeredTransition(
            AnimatorStateMachine machine,
            AnimatorState destination,
            string trigger,
            float duration)
        {
            AnimatorStateTransition transition =
                machine.AddAnyStateTransition(destination);
            transition.hasExitTime = false;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        }

        private static void AddReturnTransition(
            AnimatorState source,
            AnimatorState locomotion,
            float duration)
        {
            AnimatorStateTransition transition = source.AddTransition(locomotion);
            transition.hasExitTime = true;
            transition.exitTime = 0.95f;
            transition.hasFixedDuration = true;
            transition.duration = duration;
        }

        private static void CreateCharacterPrefab(
            string prefabPath,
            string prefabName,
            Avatar avatar,
            RuntimeAnimatorController controller,
            Material material,
            bool playable,
            bool rebuild)
        {
            if (!rebuild && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                return;

            GameObject model = RequireAsset<GameObject>(BaseModelPath);
            GameObject instance = UnityEngine.Object.Instantiate(model);
            try
            {
                instance.name = prefabName;
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                instance.transform.localScale = Vector3.one;

                Animator animator = instance.GetComponent<Animator>();
                if (animator == null)
                    animator = instance.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                        materials[i] = material;
                    renderer.sharedMaterials = materials;
                }

                if (instance.GetComponent<OmniManAnimationDriver>() == null)
                    instance.AddComponent<OmniManAnimationDriver>();

                if (playable)
                {
                    CharacterController characterController =
                        instance.AddComponent<CharacterController>();
                    ConfigureCharacterController(characterController, renderers);
                    instance.AddComponent<OmniManThirdPersonController>();
                    instance.AddComponent<OmniManThirdPersonCamera>();
                }

                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void ConfigureCharacterController(
            CharacterController characterController,
            Renderer[] renderers)
        {
            if (renderers.Length == 0)
            {
                characterController.height = 2f;
                characterController.center = Vector3.up;
                characterController.radius = 0.35f;
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            float height = Mathf.Max(1f, bounds.size.y);
            characterController.height = height;
            characterController.center =
                InstanceLocalPoint(bounds.center, characterController.transform);
            characterController.radius = Mathf.Clamp(
                Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.65f,
                0.2f,
                height * 0.35f);
            characterController.stepOffset = Mathf.Min(0.3f, height * 0.2f);
            characterController.skinWidth = Mathf.Max(0.02f, characterController.radius * 0.1f);
        }

        private static Vector3 InstanceLocalPoint(Vector3 worldPoint, Transform transform)
        {
            return transform.InverseTransformPoint(worldPoint);
        }

        private static void ValidateCharacterPrefab(
            GameObject prefab,
            bool playable)
        {
            Animator animator = prefab.GetComponent<Animator>();
            if (animator == null || animator.avatar == null ||
                !animator.avatar.isValid || !animator.avatar.isHuman)
            {
                throw new InvalidOperationException(prefab.name + " 缺少有效 Humanoid Avatar。");
            }

            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            string[] dependencies = AssetDatabase.GetDependencies(prefabPath, true);
            if (!dependencies.Contains(ControllerPath))
                throw new InvalidOperationException(prefab.name + " 未引用新状态机。");
            if (animator.applyRootMotion)
                throw new InvalidOperationException(prefab.name + " 不应启用 Root Motion。");
            if (prefab.GetComponent<OmniManAnimationDriver>() == null)
                throw new InvalidOperationException(prefab.name + " 缺少动画驱动。");

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0 || !dependencies.Contains(MaterialPath))
            {
                throw new InvalidOperationException(prefab.name + " 的材质引用异常。");
            }

            if (playable &&
                (prefab.GetComponent<CharacterController>() == null ||
                 prefab.GetComponent<OmniManThirdPersonController>() == null ||
                 prefab.GetComponent<OmniManThirdPersonCamera>() == null))
            {
                throw new InvalidOperationException(prefab.name + " 缺少第三人称组件。");
            }
        }

        private static void ExtractEmbeddedTextures()
        {
            if (FindTexturePath("texture_pbr_20250901.png") != null)
                return;

            ModelImporter importer = GetModelImporter(BaseModelPath);
            if (!importer.ExtractTextures(TextureDirectory))
                throw new InvalidOperationException("Unity 未能从基准 FBX 提取内嵌贴图。");
            AssetDatabase.Refresh();
        }

        private static Material CreateOrUpdateMaterial()
        {
            string baseColorPath = RequireTexturePath("texture_pbr_20250901.png");
            string normalPath = RequireTexturePath("texture_pbr_20250901_normal.png");
            string roughnessPath = RequireTexturePath("texture_pbr_20250901_roughness.png");
            string metallicPath = RequireTexturePath("texture_pbr_20250901_metallic.png");

            ConfigureTexture(baseColorPath, TextureImporterType.Default, true, false);
            ConfigureTexture(normalPath, TextureImporterType.NormalMap, false, false);
            ConfigureTexture(roughnessPath, TextureImporterType.Default, false, false);
            ConfigureTexture(metallicPath, TextureImporterType.Default, false, false);
            CreatePackedMetallicSmoothness(metallicPath, roughnessPath);
            ConfigureTexture(PackedMapPath, TextureImporterType.Default, false, false);

            bool useUrp = IsUniversalRenderPipelineActive();
            string shaderName = useUrp
                ? "Universal Render Pipeline/Lit"
                : "Standard";
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new InvalidOperationException("未找到兼容 Shader：" + shaderName);

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null &&
                AssetDatabase.LoadAssetAtPath<Material>(LegacyMaterialPath) != null)
            {
                string moveError =
                    AssetDatabase.MoveAsset(LegacyMaterialPath, MaterialPath);
                if (!string.IsNullOrEmpty(moveError))
                    throw new InvalidOperationException("材质重命名失败：" + moveError);
                material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            }

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            material.name = useUrp ? "OmniMan_URP" : "OmniMan_Standard";
            material.shader = shader;

            Texture2D baseColor =
                AssetDatabase.LoadAssetAtPath<Texture2D>(baseColorPath);
            Texture2D normal =
                AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            Texture2D metallicSmoothness =
                AssetDatabase.LoadAssetAtPath<Texture2D>(PackedMapPath);

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", baseColor);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", baseColor);

            material.SetTexture(
                "_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");

            material.SetTexture(
                "_MetallicGlossMap", metallicSmoothness);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f);

            material.DisableKeyword("_METALLICSPECGLOSSMAP");
            material.DisableKeyword("_METALLICGLOSSMAP");
            if (useUrp)
            {
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            else
            {
                material.EnableKeyword("_METALLICGLOSSMAP");
                if (material.HasProperty("_GlossMapScale"))
                    material.SetFloat("_GlossMapScale", 1f);
                if (material.HasProperty("_Glossiness"))
                    material.SetFloat("_Glossiness", 1f);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static bool IsUniversalRenderPipelineActive()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
                return false;

            string pipelineType = pipeline.GetType().FullName ?? string.Empty;
            if (pipelineType.Contains("UniversalRenderPipelineAsset"))
                return true;

            throw new InvalidOperationException(
                "当前脚本仅支持 Built-in 或 URP，检测到：" + pipelineType);
        }

        private static bool IsMaterialShaderCompatible(Material material)
        {
            if (material == null || material.shader == null)
                return false;

            string expectedShader = IsUniversalRenderPipelineActive()
                ? "Universal Render Pipeline/Lit"
                : "Standard";
            return material.shader.name == expectedShader;
        }

        private static void CreatePackedMetallicSmoothness(
            string metallicPath,
            string roughnessPath)
        {
            SetReadable(metallicPath, true);
            SetReadable(roughnessPath, true);

            Texture2D metallic = RequireAsset<Texture2D>(metallicPath);
            Texture2D roughness = RequireAsset<Texture2D>(roughnessPath);
            int width = metallic.width;
            int height = metallic.height;
            Color32[] metallicPixels = metallic.GetPixels32();
            Color32[] packedPixels = new Color32[metallicPixels.Length];

            if (roughness.width == width && roughness.height == height)
            {
                Color32[] roughnessPixels = roughness.GetPixels32();
                for (int i = 0; i < packedPixels.Length; i++)
                {
                    byte metal = metallicPixels[i].r;
                    packedPixels[i] =
                        new Color32(metal, metal, metal, (byte)(255 - roughnessPixels[i].r));
                }
            }
            else
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = y * width + x;
                        byte metal = metallicPixels[index].r;
                        float u = (x + 0.5f) / width;
                        float v = (y + 0.5f) / height;
                        byte smoothness = (byte)Mathf.RoundToInt(
                            (1f - roughness.GetPixelBilinear(u, v).r) * 255f);
                        packedPixels[index] =
                            new Color32(metal, metal, metal, smoothness);
                    }
                }
            }

            var packed = new Texture2D(
                width, height, TextureFormat.RGBA32, false, true);
            packed.SetPixels32(packedPixels);
            packed.Apply(false, false);
            File.WriteAllBytes(PackedMapPath, packed.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(packed);

            AssetDatabase.ImportAsset(PackedMapPath, ImportAssetOptions.ForceUpdate);
            SetReadable(metallicPath, false);
            SetReadable(roughnessPath, false);
        }

        private static ModelImporter GetModelImporter(string assetPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
                throw new FileNotFoundException("找不到 FBX ModelImporter。", assetPath);
            return importer;
        }

        private static Avatar FindAvatar(string assetPath)
        {
            return AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Avatar>()
                .FirstOrDefault();
        }

        private static AnimationClip RequireAnimationClip(
            string assetPath,
            string expectedName)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(candidate =>
                    !candidate.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (clip == null || clip.name != expectedName)
                throw new InvalidOperationException(
                    $"{assetPath} 没有正确导入为 {expectedName} AnimationClip。");
            return clip;
        }

        private static T RequireAsset<T>(string assetPath)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null)
                throw new FileNotFoundException(
                    $"没有找到 {typeof(T).Name} 资源。", assetPath);
            return asset;
        }

        private static string RequireTexturePath(string fileName)
        {
            string path = FindTexturePath(fileName);
            if (path == null)
                throw new FileNotFoundException("没有找到 FBX 提取贴图。", fileName);
            return path;
        }

        private static string FindTexturePath(string fileName)
        {
            if (!Directory.Exists(TextureDirectory))
                return null;

            string path = Directory
                .GetFiles(TextureDirectory, fileName, SearchOption.AllDirectories)
                .FirstOrDefault();
            return path?.Replace('\\', '/');
        }

        private static void ConfigureTexture(
            string path,
            TextureImporterType type,
            bool sRgb,
            bool readable)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;

            bool changed =
                importer.textureType != type ||
                importer.sRGBTexture != sRgb ||
                importer.isReadable != readable;
            importer.textureType = type;
            importer.sRGBTexture = sRgb;
            importer.isReadable = readable;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            if (changed)
                importer.SaveAndReimport();
        }

        private static void SetReadable(string path, bool readable)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.isReadable == readable)
                return;
            importer.isReadable = readable;
            importer.SaveAndReimport();
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;

            string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            string name = Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new InvalidOperationException("无效的 Unity 目录：" + assetPath);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
