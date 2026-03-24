using System;
using System.Collections.Generic;
using UnityEngine;

namespace JB.ModularCharacterBuilder
{
    [CreateAssetMenu(fileName = "CharacterBuildPreset", menuName = "João Baltieri 3D/Modular Character Build Preset")]
    public class ModularCharacterBuildPreset : ScriptableObject
    {
        public enum MaterialFamily
        {
            Unknown = 0,
            Opaque = 1,
            Transparent = 2,
            Metallic = 3,
            Emissive = 4,

            // legacy aliases kept for compatibility
            Glass = Transparent,
            Metal = Metallic
        }

        [Serializable]
        public class CategorySelection
        {
            public string categoryName;
            public bool allowMultiple;
            public bool isLocked;
            public string selectedPieceKey;
            public List<string> selectedPieceKeys = new();
            public List<string> lockedPieceKeys = new();
        }

        [Serializable]
        public class SlotMaterialState
        {
            public string slotName;
            public string rendererPath;
            public int slotIndex = -1;

            public MaterialFamily family = MaterialFamily.Unknown;
            public bool isLocked;

            public bool useCustom;
            public Material existingMaterial;

            public Color baseColor = Color.white;
            public Color emissionColor = Color.black;
        }

        [Serializable]
        public class PieceMaterialState
        {
            public string pieceKey;
            public bool isBodyGroup;
            public List<SlotMaterialState> slots = new();
        }

        public List<string> sourceCharacterNames = new();
        public List<CategorySelection> categorySelections = new();
        public List<PieceMaterialState> pieceMaterials = new();
    }
}
