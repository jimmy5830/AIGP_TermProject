using System;
using System.IO;
using System.Linq;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class CombatAnimationPrefabSetup
{
    private const string AnimationsFolder = "Assets/Animations";
    private const string AgentAControllerPath = AnimationsFolder + "/AgentACombat.controller";
    private const string AgentBControllerPath = AnimationsFolder + "/AgentBCombat.controller";

    private const string AgentAPrefabPath = "Assets/Prefabs/AgentA.prefab";
    private const string AgentBPrefabPath = "Assets/Prefabs/AgentB.prefab";

    private const string AgentAModelPath = "Assets/Resources/Punch/Ch46_nonPBR.fbx";
    private const string AgentBModelPath = "Assets/Resources/Sword/Ch03_nonPBR.fbx";

    [MenuItem("Combat/Setup Animated Agent Prefabs")]
    public static void ConfigureAll()
    {
        EnsureFolder(AnimationsFolder);

        Avatar agentAAvatar = ConfigureModelImporter(AgentAModelPath);
        Avatar agentBAvatar = ConfigureModelImporter(AgentBModelPath);

        AgentAnimationSet agentAAnimations = new AgentAnimationSet(
            "Assets/Resources/Punch/Anim/Ch46_nonPBR@Idle.fbx",
            "Assets/Resources/Punch/Anim/Ch46_nonPBR@Run.fbx",
            "Assets/Resources/Punch/Anim/Ch46_nonPBR@Punching.fbx",
            "Assets/Resources/Punch/Anim/Ch46_nonPBR@Dodging Right.fbx",
            "Assets/Resources/Punch/Anim/Ch46_nonPBR@Head Hit.fbx",
            "Assets/Resources/Punch/Anim/Ch46_nonPBR@Standing Block Idle.fbx",
            agentAAvatar);

        AgentAnimationSet agentBAnimations = new AgentAnimationSet(
            "Assets/Resources/Sword/Anim/Ch03_nonPBR@Great Sword Idle.fbx",
            "Assets/Resources/Sword/Anim/Ch03_nonPBR@Great Sword Walk.fbx",
            "Assets/Resources/Sword/Anim/Ch03_nonPBR@Great Sword Slash.fbx",
            "Assets/Resources/Sword/Anim/Ch03_nonPBR@Dodging Right.fbx",
            "Assets/Resources/Sword/Anim/Ch03_nonPBR@Great Sword Impact.fbx",
            "Assets/Resources/Sword/Anim/Ch03_nonPBR@Great Sword Blocking.fbx",
            agentBAvatar);

        ConfigureAnimationImporters(agentAAnimations);
        ConfigureAnimationImporters(agentBAnimations);

        AnimatorController agentAController = CreateController(AgentAControllerPath, agentAAnimations);
        AnimatorController agentBController = CreateController(AgentBControllerPath, agentBAnimations);

        ConfigureAgentPrefab(
            AgentAPrefabPath,
            "AgentA",
            agentAController,
            agentAAvatar,
            isAgentA: true);

        ConfigureAgentPrefab(
            AgentBPrefabPath,
            "AgentB",
            agentBController,
            agentBAvatar,
            isAgentA: false);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Configured animated AgentA/AgentB prefabs. Set opponent references manually in scene instances and add OnAttackHitFrame/OnAttackEnd Animation Events to attack clips.");
    }

    public static void ConfigureAllForBatch()
    {
        ConfigureAll();
    }

    private static Avatar ConfigureModelImporter(string modelPath)
    {
        ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null)
        {
            throw new InvalidOperationException($"Missing model importer: {modelPath}");
        }

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.SaveAndReimport();

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath)
            .OfType<Avatar>()
            .FirstOrDefault(candidate => candidate.isHuman);

        if (avatar == null)
        {
            throw new InvalidOperationException($"Could not load humanoid avatar from {modelPath}.");
        }

        return avatar;
    }

    private static void ConfigureAnimationImporters(AgentAnimationSet animations)
    {
        ConfigureAnimationImporter(animations.IdlePath, animations.Avatar, loopTime: true);
        ConfigureAnimationImporter(animations.MovePath, animations.Avatar, loopTime: true);
        ConfigureAnimationImporter(animations.AttackPath, animations.Avatar, loopTime: false);
        ConfigureAnimationImporter(animations.DodgePath, animations.Avatar, loopTime: false);
        ConfigureAnimationImporter(animations.HitPath, animations.Avatar, loopTime: false);
        ConfigureAnimationImporter(animations.BlockPath, animations.Avatar, loopTime: true);
    }

    private static void ConfigureAnimationImporter(string clipPath, Avatar avatar, bool loopTime)
    {
        ModelImporter importer = AssetImporter.GetAtPath(clipPath) as ModelImporter;
        if (importer == null)
        {
            throw new InvalidOperationException($"Missing animation importer: {clipPath}");
        }

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;
        importer.importAnimation = true;

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.clipAnimations;
        }

        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].loopTime = loopTime;
            clips[i].loopPose = loopTime;
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    private static AnimatorController CreateController(string path, AgentAnimationSet animations)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
        {
            AssetDatabase.DeleteAsset(path);
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("IsBlocking", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Dodge", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

        AnimatorState idle = AddState(stateMachine, "Idle", LoadClip(animations.IdlePath), new Vector3(250f, 120f));
        AnimatorState move = AddState(stateMachine, "Move", LoadClip(animations.MovePath), new Vector3(500f, 120f));
        AnimatorState attack = AddState(stateMachine, "Attack", LoadClip(animations.AttackPath), new Vector3(250f, 300f));
        AnimatorState dodge = AddState(stateMachine, "Dodge", LoadClip(animations.DodgePath), new Vector3(500f, 300f));
        AnimatorState hit = AddState(stateMachine, "Hit", LoadClip(animations.HitPath), new Vector3(750f, 300f));
        AnimatorState block = AddState(stateMachine, "Block", LoadClip(animations.BlockPath), new Vector3(750f, 120f));

        stateMachine.defaultState = idle;

        AddBoolTransition(idle, move, "IsMoving", true);
        AddBoolTransition(move, idle, "IsMoving", false);
        AddBoolTransition(block, idle, "IsBlocking", false);

        AddAnyStateTriggerTransition(stateMachine, attack, "Attack");
        AddAnyStateTriggerTransition(stateMachine, dodge, "Dodge");
        AddAnyStateTriggerTransition(stateMachine, hit, "Hit");
        AddAnyStateBoolTransition(stateMachine, block, "IsBlocking", true);

        AddExitTransition(attack, idle);
        AddExitTransition(dodge, idle);
        AddExitTransition(hit, idle);

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static AnimatorState AddState(AnimatorStateMachine stateMachine, string name, AnimationClip clip, Vector3 position)
    {
        AnimatorState state = stateMachine.AddState(name, position);
        state.motion = clip;
        state.writeDefaultValues = true;
        return state;
    }

    private static void AddBoolTransition(AnimatorState from, AnimatorState to, string parameter, bool value)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0.1f;
        transition.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, parameter);
    }

    private static void AddAnyStateTriggerTransition(AnimatorStateMachine stateMachine, AnimatorState to, string trigger)
    {
        AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(to);
        transition.canTransitionToSelf = false;
        transition.hasExitTime = false;
        transition.duration = 0.05f;
        transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
    }

    private static void AddAnyStateBoolTransition(AnimatorStateMachine stateMachine, AnimatorState to, string parameter, bool value)
    {
        AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(to);
        transition.canTransitionToSelf = false;
        transition.hasExitTime = false;
        transition.duration = 0.05f;
        transition.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, parameter);
    }

    private static void AddExitTransition(AnimatorState from, AnimatorState to)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = 0.95f;
        transition.duration = 0.1f;
    }

    private static AnimationClip LoadClip(string path)
    {
        AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(candidate => !candidate.name.StartsWith("__preview__", StringComparison.Ordinal));

        if (clip == null)
        {
            throw new InvalidOperationException($"Could not load animation clip from {path}.");
        }

        return clip;
    }

    private static void ConfigureAgentPrefab(
        string prefabPath,
        string prefabName,
        RuntimeAnimatorController controller,
        Avatar avatar,
        bool isAgentA)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            root.name = prefabName;

            Rigidbody body = GetOrAdd<Rigidbody>(root);
            body.mass = 1f;
            body.useGravity = true;
            body.isKinematic = false;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            CapsuleCollider collider = GetOrAdd<CapsuleCollider>(root);
            collider.radius = 0.5f;
            collider.height = 2f;
            collider.center = new Vector3(0f, 1f, 0f);

            Animator animator = GetOrAdd<Animator>(root);
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            SetObject(animator, "m_Avatar", avatar);

            CombatCharacter character = GetOrAdd<CombatCharacter>(root);
            CooldownSystem cooldownSystem = GetOrAdd<CooldownSystem>(root);
            CombatHitDetector hitDetector = GetOrAdd<CombatHitDetector>(root);
            CombatAnimatorDriver animatorDriver = GetOrAdd<CombatAnimatorDriver>(root);
            CombatActionController actionController = GetOrAdd<CombatActionController>(root);

            SetObject(animatorDriver, "animator", animator);

            SetFloat(actionController, "moveSpeed", isAgentA ? 1f : 4f);
            SetFloat(actionController, "dodgeImpulse", 7f);
            SetFloat(actionController, "dodgeInvincibleDuration", 0.35f);
            SetFloat(actionController, "blockDuration", 0.75f);
            SetObject(actionController, "body", body);
            SetObject(actionController, "cooldownSystem", cooldownSystem);
            SetObject(actionController, "character", character);
            SetObject(actionController, "hitDetector", hitDetector);
            SetObject(actionController, "animatorDriver", animatorDriver);

            SetObject(character, "actionController", actionController);
            SetObject(character, "cooldownSystem", cooldownSystem);

            SetObject(hitDetector, "owner", character);
            SetObject(hitDetector, "target", null);
            SetFloat(hitDetector, "attackRange", 2f);
            SetFloat(hitDetector, "attackDamage", 20f);
            SetFloat(hitDetector, "attackAngle", 90f);
            SetBool(hitDetector, "drawAttackRays", true);

            SetFloat(cooldownSystem, "attackCooldown", 2.5f);
            SetFloat(cooldownSystem, "blockCooldown", 2.5f);
            SetFloat(cooldownSystem, "dodgeCooldown", 5f);

            ConfigureControllers(root, character, cooldownSystem, actionController, isAgentA);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureControllers(
        GameObject root,
        CombatCharacter character,
        CooldownSystem cooldownSystem,
        CombatActionController actionController,
        bool isAgentA)
    {
        ManualCombatInput manualInput = GetOrAdd<ManualCombatInput>(root);
        manualInput.enabled = false;
        SetObject(manualInput, "actionController", actionController);

        if (isAgentA)
        {
            BaselineAttackerBT attackerBT = GetOrAdd<BaselineAttackerBT>(root);
            attackerBT.enabled = true;
            SetObject(attackerBT, "self", character);
            SetObject(attackerBT, "target", null);
            SetObject(attackerBT, "actionController", actionController);
            SetObject(attackerBT, "cooldownSystem", cooldownSystem);
            SetFloat(attackerBT, "attackDistance", 1.8f);
            SetFloat(attackerBT, "facingAngle", 45f);
            SetFloat(attackerBT, "lowHealthRatio", 0.35f);

            ConfigureMlAgent(root, character, cooldownSystem, actionController);
        }
        else
        {
            BaselineDefenderBT defenderBT = GetOrAdd<BaselineDefenderBT>(root);
            defenderBT.enabled = true;
            SetObject(defenderBT, "self", character);
            SetObject(defenderBT, "target", null);
            SetObject(defenderBT, "actionController", actionController);
            SetObject(defenderBT, "cooldownSystem", cooldownSystem);
            SetFloat(defenderBT, "closeDistance", 2f);
            SetFloat(defenderBT, "preferredDistance", 3f);
            SetFloat(defenderBT, "lowHealthRatio", 0.3f);
        }
    }

    private static void ConfigureMlAgent(
        GameObject root,
        CombatCharacter character,
        CooldownSystem cooldownSystem,
        CombatActionController actionController)
    {
        StudentCombatAgent studentAgent = GetOrAdd<StudentCombatAgent>(root);
        studentAgent.enabled = false;
        SetObject(studentAgent, "self", character);
        SetObject(studentAgent, "opponent", null);
        SetObject(studentAgent, "actionController", actionController);
        SetObject(studentAgent, "cooldownSystem", cooldownSystem);
        SetObject(studentAgent, "episodeManager", null);
        SetBool(studentAgent, "resetEpisodeOnBegin", false);
        SetBool(studentAgent, "isPrimaryResetAgent", false);
        SetBool(studentAgent, "enableInternalTestReward", false);
        SetBool(studentAgent, "enableStudentTestReward", true);
        SetFloat(studentAgent, "observationDistance", 10f);

        BehaviorParameters behaviorParameters = GetOrAdd<BehaviorParameters>(root);
        behaviorParameters.enabled = false;
        behaviorParameters.BehaviorName = "CombatAgent";
        behaviorParameters.BehaviorType = BehaviorType.Default;
        behaviorParameters.BrainParameters.VectorObservationSize = 15;
        behaviorParameters.BrainParameters.NumStackedVectorObservations = 1;
        behaviorParameters.BrainParameters.ActionSpec = new ActionSpec(0, new[] { 5, 4 });
        EditorUtility.SetDirty(behaviorParameters);

        DecisionRequester decisionRequester = GetOrAdd<DecisionRequester>(root);
        decisionRequester.enabled = false;
        decisionRequester.DecisionPeriod = 5;
        decisionRequester.DecisionStep = 0;
        decisionRequester.TakeActionsBetweenDecisions = true;
        EditorUtility.SetDirty(decisionRequester);
    }

    private static T GetOrAdd<T>(GameObject root) where T : Component
    {
        T component = root.GetComponent<T>();
        return component != null ? component : root.AddComponent<T>();
    }

    private static void SetObject(UnityEngine.Object target, string propertyPath, UnityEngine.Object value)
    {
        SerializedProperty property = FindProperty(target, propertyPath);
        property.objectReferenceValue = value;
        property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetFloat(UnityEngine.Object target, string propertyPath, float value)
    {
        SerializedProperty property = FindProperty(target, propertyPath);
        property.floatValue = value;
        property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetBool(UnityEngine.Object target, string propertyPath, bool value)
    {
        SerializedProperty property = FindProperty(target, propertyPath);
        property.boolValue = value;
        property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static SerializedProperty FindProperty(UnityEngine.Object target, string propertyPath)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyPath);
        if (property == null)
        {
            throw new InvalidOperationException($"Could not find serialized property '{propertyPath}' on {target.GetType().Name}.");
        }

        return property;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
        string folder = Path.GetFileName(folderPath);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folder))
        {
            throw new InvalidOperationException($"Invalid folder path: {folderPath}");
        }

        AssetDatabase.CreateFolder(parent, folder);
    }

    private readonly struct AgentAnimationSet
    {
        public AgentAnimationSet(
            string idlePath,
            string movePath,
            string attackPath,
            string dodgePath,
            string hitPath,
            string blockPath,
            Avatar avatar)
        {
            IdlePath = idlePath;
            MovePath = movePath;
            AttackPath = attackPath;
            DodgePath = dodgePath;
            HitPath = hitPath;
            BlockPath = blockPath;
            Avatar = avatar;
        }

        public string IdlePath { get; }
        public string MovePath { get; }
        public string AttackPath { get; }
        public string DodgePath { get; }
        public string HitPath { get; }
        public string BlockPath { get; }
        public Avatar Avatar { get; }
    }
}
