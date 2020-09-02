using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ShaderGraphPropertyRenamer
{
    [Serializable]
    public class ShaderPropertyRename
    {
        private const string kSearchStringReferenceProperty = "m_OverrideReferenceName";
        private const string kSearchStringNameProperty = "m_Name";
        [SerializeField]
        private string m_reference;
        [SerializeField]
        private string m_name;
        [SerializeField]
        private string m_newReference;
        [SerializeField]
        private string m_newName;
        [SerializeField]
        private string m_searchStringReference;
        [SerializeField]
        private string m_searchStringName;
        public bool isModified;
        public bool nameModified;
        public bool referenceModified;
        public bool isHiddenProperty;
        public bool isToggle;
        public UnityEngine.Rendering.ShaderPropertyType propertyType;
        public bool isEnumKeyword = false;
        public List<string> keywordValues;
        public bool isDuplicate = false;
        public bool isSwitched = false;


        public ShaderPropertyRename(string reference, string name,
            UnityEngine.Rendering.ShaderPropertyType getPropertyType,bool isEnumKeyword, bool isHidden, bool isToggle,List<string> keywordValues=null)
        {
            m_reference = m_newReference = reference;
            m_name = m_newName = name;
            m_searchStringReference = GetSearchStringReference(reference);
            m_searchStringName = GetSearchStringName(name);
            isHiddenProperty = isHidden;
            propertyType = getPropertyType;
            this.isEnumKeyword = isEnumKeyword;
            this.isToggle = isToggle;
            this.keywordValues = keywordValues;

        }
        
        public string Reference => m_reference;
        public string Name => m_name;
        public string SearchStringReference => m_searchStringReference;
        public string SearchStringName => m_searchStringName;

        public string NewReference
        {
            get => m_newReference;
            set
            {
                referenceModified = (m_reference != value);
                m_newReference = value;
                m_searchStringReference = GetSearchStringReference(value);
                isModified = nameModified || referenceModified;
            }
        }
        public string NewName
        {
            get => m_newName;
            set
            {
                nameModified = m_name != value;
                m_newName = value;
                m_searchStringName = GetSearchStringName(value);
                isModified = nameModified || referenceModified;
            }
        }
        
        public string GetSearchStringReference(string reference)
        {
            return string.Format("    \"{0}\": \"{1}\",",kSearchStringReferenceProperty,reference);
        }
        public string GetSearchStringName(string name)
        {
            return string.Format("    \"{0}\": \"{1}\",",kSearchStringNameProperty,name);
        }
    }
}