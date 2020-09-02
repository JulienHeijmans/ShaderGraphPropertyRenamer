
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using System.IO;
using System.Linq;
using NUnit.Framework.Constraints;
using UnityEditor.Rendering;
//using UnityEditor.Rendering.HighDefinition;
using UnityEditor.UIElements;
using UnityEditor.VersionControl;
using UnityEngine.Rendering;

namespace ShaderGraphPropertyRenamer
{
    [Serializable]
    public class ShaderGraphPropertyRenamerWindow : EditorWindow
    {
        private VisualElement root;
        private bool initiated=false;
        static ShaderGraphPropertyRenamerWindow m_Window;
        [SerializeField]
        private Shader m_shader;

        public Shader Shader => m_shader;

        private Shader m_shaderOld;

        private VisualElement ui_PropertyListContainer;
        private VisualElement ui_ListViewContentContainer;

        private ObjectField ui_ObjectField_SelectedShader;
        private Button ui_Button_Apply;
        private Button ui_Button_Refresh;
        private Button ui_Button_Reset;
        private Button ui_Button_Checkout;
        private Button ui_Button_Lock;
        private Toggle ui_Toggle_DisplayHiddenAttributes;
        private Toggle ui_Toggle_ClearMaterialProperties;
        [SerializeField]
        private bool m_DisplayHidden;
        [SerializeField]
        private List<ShaderPropertyRename> m_ShaderProperties;
        [SerializeField]
        private List<ShaderPropertyRename> m_ShaderPropertiesNoHidden;
        private ListView m_ListView;
        private int m_ModifiedPropertyCount = 0;
        private int m_HiddenModifiedPropertyCount = 0;
        private int m_MaterialCount = 0;
        private Label ui_Label_ModifiedCount;
        private Label ui_Label_WarningHiddenModified;
        private Label ui_Label_MaterialCount;
        private Label ui_Label_NormalShaderSelected;
        [SerializeField]
        private DictionaryString m_OverrideNameBackup;        
        [SerializeField]
        private DictionaryString m_OverridenReferenceBackup;

        private AffectedFilesWindow m_affectedFileWindow;
        public event EventHandler onUpdateMaterialList;

        public List<Material> m_MaterialList;
        [SerializeField]
        private bool m_clearUnusedProperties = false;

        private bool m_HasLockingSupport = false;
        public Task m_VersionControlUpdate;

        public int ModifiedPropertyCount
        {
            get => m_ModifiedPropertyCount;
            set
            {
                m_ModifiedPropertyCount = value;
                ui_Label_ModifiedCount.text = value.ToString();
            }
        }
        public int HiddenModifiedPropertyCount
        {
            get => m_HiddenModifiedPropertyCount;
            set
            {
                m_HiddenModifiedPropertyCount = value;
                ui_Label_WarningHiddenModified.text = string.Format("(Warning: {0} {1} modified)",value,value>1?"hidden properties are":"hidden property is");
                if(!m_DisplayHidden)
                    ui_Label_WarningHiddenModified.style.display = value > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
        public int MaterialCount
        {
            get => m_MaterialCount;
            set
            {
                m_MaterialCount = value;
                ui_Label_MaterialCount.text = value.ToString();
            }
        }

        

        [MenuItem("Tools/ShaderGraph Property Renamer")]
        public static void ShowWindow()
        {
            ShaderGraphPropertyRenamerWindow window = CreateInstance<ShaderGraphPropertyRenamerWindow>();
            window.name = "ShaderGraph Property Renamer";
            window.titleContent = new GUIContent("ShaderGraph Property Renamer");
            window.Show();
        }
        


        private void OnEnable()
        {
            if(m_OverrideNameBackup==null)
                m_OverrideNameBackup=new DictionaryString();
            if(m_OverridenReferenceBackup==null)
                m_OverridenReferenceBackup=new DictionaryString();
            
            this.minSize = new Vector2(415, 350);
            string scriptPath = Utility.GetScriptPath("ShaderGraphPropertyRenamerWindow");
            VisualTreeAsset uiAsset =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(scriptPath + "ShaderGraphPropertyRenamerWindow.uxml");
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(scriptPath + "ShaderGraphPropertyRenamerWindow.uss");
            VisualElement ui = uiAsset.CloneTree();
            ui.style.flexGrow = 1f;
            root = rootVisualElement;
            root.style.flexGrow = 1;
            root.Add(ui);
            root.styleSheets.Add(uss);
            
            Type providerType = typeof(Provider);
            m_HasLockingSupport = providerType.GetProperty("hasLockingSupport") != null;
            if (m_HasLockingSupport)
                m_HasLockingSupport = (bool) typeof(Provider).GetProperty("hasLockingSupport").GetValue(null);

            InitUI();
            initiated = true;
            UpdatePropertyList();
            CheckDuplicate();
            CheckVersionControlStatus();
        }
        

        private void OnDisable()
        {
            if(m_affectedFileWindow!=null && m_affectedFileWindow.IsOpen)
                m_affectedFileWindow.Close();
        }



        public void InitUI()
        {
            ui_PropertyListContainer = root.Query<VisualElement>("ObjectListContainer").First();
            ui_ObjectField_SelectedShader = root.Query<ObjectField>("ObjectField_SelectedShader").First();
            ui_ObjectField_SelectedShader.value = m_shader;
            ui_ObjectField_SelectedShader.RegisterValueChangedCallback(e =>
            {
                bool differentShader = m_shader != e.newValue;
                m_shader = (Shader) e.newValue;
                UpdatePropertyList(differentShader);
            });
            ui_Toggle_DisplayHiddenAttributes = root.Query<Toggle>("Toggle_DisplayHiddenAttributes").First();
            ui_Toggle_DisplayHiddenAttributes.RegisterValueChangedCallback(e =>
            {
                m_DisplayHidden = e.newValue;
                m_ListView.itemsSource = e.newValue ? m_ShaderProperties : m_ShaderPropertiesNoHidden;
                if (!e.newValue && m_HiddenModifiedPropertyCount > 0)
                    ui_Label_WarningHiddenModified.style.display = DisplayStyle.Flex;
                else
                    ui_Label_WarningHiddenModified.style.display = DisplayStyle.None;
                m_ListView.Refresh();
                m_ListView.MarkDirtyRepaint();
                m_ListView.SetEnabled(false);
                m_ListView.SetEnabled(true);
            });
            ui_Toggle_ClearMaterialProperties = root.Query<Toggle>("Toggle_ClearMaterialProperties").First();
            ui_Toggle_ClearMaterialProperties.value = m_clearUnusedProperties;
            ui_Toggle_ClearMaterialProperties.RegisterValueChangedCallback(e =>
            {
                m_clearUnusedProperties = e.newValue;
            });
            ui_Button_Apply = root.Query<Button>("Button_Apply").First();
            ui_Button_Apply.clicked += Apply;
            ui_Button_Refresh = root.Query<Button>("Button_Refresh").First();
            ui_Button_Refresh.clicked += ()=>{UpdatePropertyList();};
            ui_Button_Reset = root.Query<Button>("Button_Reset").First();
            ui_Button_Reset.clicked += ()=>{UpdatePropertyList(true);};
            var ui_Button_FileList = root.Query<Button>("Button_FileList").First();
            ui_Button_FileList.clicked += () =>
            {
                if (m_affectedFileWindow == null)
                {
                    m_affectedFileWindow =  EditorWindow.GetWindow<AffectedFilesWindow>(); 
                    m_affectedFileWindow.Init(this);
                }
                
            };
            ui_Button_Checkout = root.Query<Button>("Button_Checkout").First();
            ui_Button_Checkout.clicked += ()=>{CheckOutAllFiles(true);};
            ui_Button_Lock = root.Query<Button>("Button_Lock").First();
            ui_Button_Lock.clicked += ()=>{CheckOutAllFiles(true);};

            ui_Label_ModifiedCount = root.Query<Label>("Label_ModifiedCount").First();
            ui_Label_MaterialCount = root.Query<Label>("Label_MaterialCount").First();
            ui_Label_NormalShaderSelected = root.Query<Label>("Label_NormalShaderSelected").First();
            ui_Label_WarningHiddenModified = root.Query<Label>("Label_WarningHiddenModified").First();
            if (!(Provider.isActive && Provider.enabled))
            {
                ui_Button_Checkout.SetEnabled(false);
                ui_Button_Lock.SetEnabled(false);
                ui_Button_Checkout.tooltip =
                    "Version Control Disabled. Enable it in the Project Settings and re-open this window.";
                ui_Button_Lock.tooltip =
                    "Version Control Disabled. Enable it in the Project Settings and re-open this window.";
            }
        }



        private void UpdatePropertyList(bool clear=false)
        {

            if (m_shader == null)
            {
                clear = true;
            }
            
            var shaderPath = AssetDatabase.GetAssetPath(m_shader);
            bool isShaderGraph = shaderPath.EndsWith(".shadergraph", true, CultureInfo.CurrentCulture);
            if (!isShaderGraph && m_shader!=null)
            {
                clear = true;
                ui_Label_NormalShaderSelected.style.display = DisplayStyle.Flex;
            }
            else
            {
                ui_Label_NormalShaderSelected.style.display = DisplayStyle.None;
            }
            
            if (m_ShaderProperties == null)
            {
                m_ShaderProperties=new List<ShaderPropertyRename>();
                m_ShaderPropertiesNoHidden = new List<ShaderPropertyRename>();
                clear = true;
            }

            if (m_ListView != null)
            {
                m_ListView.RemoveFromHierarchy();
                m_ListView = null;
            }
            m_ShaderProperties.Clear();
            m_ShaderPropertiesNoHidden.Clear();
            ModifiedPropertyCount = 0;
            HiddenModifiedPropertyCount = 0;
            MaterialCount = 0;
            if (clear)
            {
                m_OverrideNameBackup.Clear();
                m_OverridenReferenceBackup.Clear();
                if (m_shader == null || !isShaderGraph)
                    return;
                //Debug.Log("[ShaderGraphPropertyRenamed] Shader properties loaded: "+m_shader.name);
            }
            //else
                //Debug.Log("[ShaderGraphPropertyRenamed] Refreshing shader properties: "+m_shader.name);
            
                
            
            var propertyCount = m_shader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                bool isHidden= m_shader.GetPropertyFlags(i)==ShaderPropertyFlags.HideInInspector;
                bool isKeyword = false;
                bool isToggle = false;
                List<string> keywordValues=null;
                foreach (var attr in m_shader.GetPropertyAttributes(i))
                {
                    //Debug.Log(m_shader.GetPropertyName(i)+" - attr="+attr);

                    if (attr.Contains("Keyword"))
                    {
                        isKeyword = true;
                        string values = attr.Substring(attr.IndexOf("(") + 1, attr.IndexOf(")")-attr.IndexOf("(")-1);
                        values=values.Replace(", ", ",");
                        var valuesArr = values.Split(',');
                        keywordValues = valuesArr.ToList();
                    }
                    if (attr.Contains("Toggle"))
                        isToggle = true;
                }
                //Debug.Log(m_shader.GetPropertyName(i)+" - "+m_shader.GetPropertyType(i)+" - isKeyword="+isKeyword+" - "+m_shader.GetPropertyFlags(i));
                var newProperty = new ShaderPropertyRename(m_shader.GetPropertyName(i),
                    m_shader.GetPropertyDescription(i), m_shader.GetPropertyType(i),isKeyword,isHidden,isToggle,keywordValues);
                m_ShaderProperties.Add(newProperty);
                if(!isHidden)
                    m_ShaderPropertiesNoHidden.Add(newProperty);
            }

            if (!clear)
            {
                GetBackup();
            }
            
            
            //CreateListView
            Func<VisualElement> makeItem = () => new VisualElement();
            //ListView item update
            Action<VisualElement, int> bindItem = (e, i) =>
            {
                var currentProperty = m_DisplayHidden ? m_ShaderProperties[i] : m_ShaderPropertiesNoHidden[i];
                e.userData = i;

                VisualElement ui_PropertyLabelContainer=new VisualElement();
                ui_PropertyLabelContainer.AddToClassList("propertyLabelContainer");
                e.EnableInClassList("hiddenProperty",currentProperty.isHiddenProperty);
                
                e.AddToClassList("propertyLine");
                e.EnableInClassList("even",i%2==0);
                e.EnableInClassList("duplicate",currentProperty.isDuplicate);

                e.Clear();
                
                
                
                var ui_hiddenLabel=new Label("Hidden");
                ui_hiddenLabel.AddToClassList("hiddenLabel");
                ui_PropertyLabelContainer.Add(ui_hiddenLabel);
                
                var ui_LabelReference=new Label(currentProperty.Reference);
                ui_LabelReference.AddToClassList("labelReference");
                ui_LabelReference.EnableInClassList("hiddenProperty",currentProperty.isHiddenProperty);
                ui_PropertyLabelContainer.Add(ui_LabelReference);
                e.Add(ui_PropertyLabelContainer);
                
                var ui_TextField_NewReference=new TextField(""){value = currentProperty.NewReference};
                ui_TextField_NewReference.EnableInClassList("modified", currentProperty.referenceModified);
                ui_TextField_NewReference.AddToClassList("newReferenceField");
                ui_TextField_NewReference.tooltip = "Original Reference:"+currentProperty.Reference;
                ui_TextField_NewReference.RegisterValueChangedCallback(value =>
                {
                    bool wasModified = currentProperty.isModified;
                    currentProperty.NewReference = value.newValue;
                    
                    //Update Modified Classes
                    ui_TextField_NewReference.EnableInClassList("modified", currentProperty.referenceModified);
                    e.EnableInClassList("modified", currentProperty.isModified);

                    //Update Modified Count
                    if (!wasModified && currentProperty.isModified)
                    {
                        ModifiedPropertyCount++;
                        if (currentProperty.isHiddenProperty)
                            HiddenModifiedPropertyCount++;
                    }
                    else if (wasModified && !currentProperty.isModified)
                    {
                        ModifiedPropertyCount--;
                        if (currentProperty.isHiddenProperty)
                            HiddenModifiedPropertyCount--;
                    }
                    
                    //Store Override Backup
                    if (currentProperty.isModified)
                        m_OverridenReferenceBackup[currentProperty.Reference] = value.newValue;
                    else
                        m_OverridenReferenceBackup.Remove(currentProperty.Reference);
                    
                    //Check for duplicate
                    CheckDuplicate();
                });
                var ui_TextField_NewName=new TextField(){value = currentProperty.isHiddenProperty?"":currentProperty.NewName};
                if (currentProperty.isHiddenProperty)
                    ui_TextField_NewName.SetEnabled(false);
                ui_TextField_NewName.EnableInClassList("modified", currentProperty.nameModified);
                ui_TextField_NewName.tooltip = "Original Name:"+currentProperty.Name;

                ui_TextField_NewName.RegisterValueChangedCallback(value =>
                {
                    bool wasModified = currentProperty.isModified;
                    currentProperty.NewName = value.newValue;
                    
                    
                    //Update Modified Classes
                    ui_TextField_NewName.EnableInClassList("modified", currentProperty.nameModified);
                    e.EnableInClassList("modified", currentProperty.isModified);
                    
                    //Update Modified Count
                    if (!wasModified && currentProperty.isModified)
                    {
                        ModifiedPropertyCount++;
                        if (currentProperty.isHiddenProperty)
                            HiddenModifiedPropertyCount++;
                    }
                    else if (wasModified && !currentProperty.isModified)
                    {
                        ModifiedPropertyCount--;
                        if (currentProperty.isHiddenProperty)
                            HiddenModifiedPropertyCount--;
                    }
                    
                    //Store Override Backup
                    if (currentProperty.isModified)
                        m_OverrideNameBackup[currentProperty.Reference] = value.newValue;
                    else
                        m_OverrideNameBackup.Remove(currentProperty.Reference);
                });
                e.EnableInClassList("modified", currentProperty.isModified);

                e.Add(ui_TextField_NewReference);
                e.Add(ui_TextField_NewName);
                
                VisualElement duplicateWarning=new VisualElement();
                duplicateWarning.AddToClassList("duplicateWarning");
                duplicateWarning.tooltip = "More than one property has the same reference.";
                
                var ui_Button_ResetLine=new Button(){text = "x"};
                ui_Button_ResetLine.AddToClassList("resetLine");
                ui_Button_ResetLine.RemoveFromClassList("unity-button");
                ui_Button_ResetLine.clicked += () =>
                {
                    ui_TextField_NewReference.value = currentProperty.Reference;
                    ui_TextField_NewName.value = currentProperty.Name;
                    //currentProperty.NewReference = currentProperty.Reference;
                    //currentProperty.NewName = currentProperty.Name;
                    //ModifiedPropertyCount--;
                    //if (currentProperty.isHiddenProperty)
                    //    HiddenModifiedPropertyCount--;
                    //CheckDuplicate();
                };
                e.Add(ui_Button_ResetLine);
                
                e.Add(duplicateWarning);
            };

            // Provide the list view with an explict height for every row
            // so it can calculate how many items to actually display
            const int itemHeight = 20;

            if (m_DisplayHidden)
            {
                m_ListView = new ListView(m_ShaderProperties, itemHeight, makeItem, bindItem);
            }
            else
            {
                m_ListView = new ListView(m_ShaderPropertiesNoHidden, itemHeight, makeItem, bindItem);
            }
            m_ListView.selectionType = SelectionType.None;
            m_ListView.style.flexGrow = 1.0f;

            ui_PropertyListContainer.Add(m_ListView);
            ui_ListViewContentContainer = m_ListView.Q("unity-content-container");
            
            UpdateMaterialList();
            
        }

        private void GetBackup()
        {
            if (m_OverrideNameBackup == null || m_OverridenReferenceBackup == null)
                return;

            m_ModifiedPropertyCount = 0;
            m_HiddenModifiedPropertyCount = 0;
            foreach (var property in m_ShaderProperties)
            {
                string currentReference = property.Reference;
                
                bool isOverriden = false;
                if (m_OverridenReferenceBackup.ContainsKey(currentReference))
                {
                    property.NewReference = m_OverridenReferenceBackup[currentReference];
                    isOverriden = true;
                }
                if (m_OverrideNameBackup.ContainsKey(currentReference))
                {
                    property.NewName = m_OverrideNameBackup[currentReference];
                    isOverriden = true;
                }

                if (isOverriden)
                {
                    ModifiedPropertyCount++;
                    if (property.isHiddenProperty)
                        HiddenModifiedPropertyCount++;
                }
            }
        }

        private void CheckDuplicate()
        {
            Dictionary<string, List<ShaderPropertyRename>> duplicatesCheckDict=new Dictionary<string, List<ShaderPropertyRename>>();
            foreach (var property in m_ShaderProperties)
            {
                if (duplicatesCheckDict.ContainsKey(property.NewReference))
                {
                    duplicatesCheckDict[property.NewReference].Add(property);
                }
                else
                {
                    duplicatesCheckDict[property.NewReference]=new List<ShaderPropertyRename>();
                    duplicatesCheckDict[property.NewReference].Add(property);
                }
            }

            var duplicatesFound = duplicatesCheckDict.Where(p => p.Value.Count > 1).ToDictionary(p => p.Key, p => p.Value);;
            foreach (var property in m_ShaderProperties)
            {
                property.isDuplicate = duplicatesFound.ContainsKey(property.NewReference);
            }
            
            //Checking visible elements in the listView.
            if (ui_ListViewContentContainer != null && ui_ListViewContentContainer.childCount != 0)
            {
                foreach (var element in ui_ListViewContentContainer.Children())
                {
                    int propertyId = (int)element.userData;
                    element.EnableInClassList("duplicate",m_ShaderProperties[propertyId].isDuplicate);
                }
            }
            
        }
        private void CheckSwitch() //TODO
        {
            foreach (var property in m_ShaderProperties)
            {
                property.isSwitched = false;
            }
            Dictionary<string, List<ShaderPropertyRename>> duplicatesCheckDict=new Dictionary<string, List<ShaderPropertyRename>>();
            var modifiedProperties = m_ShaderProperties.Where(p => p.isModified == true);
            foreach (var property in modifiedProperties)
            {
                foreach (var property2 in modifiedProperties)
                {

                    if (property == property2)
                        continue;
                    if (property.Reference == property2.NewReference || property2.Reference == property.NewReference)
                    {
                        //property
                    }
                }
            }
        }

        private void Apply()
        {
            if (m_shader == null)
                return;
            
            if(ModifiedPropertyCount==0 && !m_clearUnusedProperties)
            {
                EditorUtility.DisplayDialog("No changes",
                    "No properties have been renamed.","Ok");
                return;
            }
            
            var empty = m_ShaderProperties.Where(p => (p.NewReference=="" || p.NewName=="")).ToList();
            if (empty != null && empty.Count > 0)
            {
                EditorUtility.DisplayDialog("Empty reference or name found",
                    "Some properties have empty reference or name.\nProperties references and a names cannot be empty.","Ok");
                return;
            }
            
            var duplicates = m_ShaderProperties.Where(p => p.isDuplicate).ToList();
            if (duplicates != null && duplicates.Count > 0)
            {
                EditorUtility.DisplayDialog("Duplicated Reference Found",
                    "Several properties have the same reference.\nEach property reference should be unique.","Ok");
                return;
            }
                

            //Check Remote Lock
            if (!CheckRemoteLock())
                return;
            
            //Check VCS status:
            if (!CheckConflict())
                return;
            if (!CheckOutAllFiles(false))
                return;
            
            
            UpdateMaterialList();
            
            if(ModifiedPropertyCount>0) //Edit Shader only if properties have been renamed
            {
                if (!UpdateShader())
                {
                    Debug.Log("[ShaderGraphPropertyRename] Error while renaming shader properties.");
                    //AssetDatabase.StopAssetEditing();
                    return;
                }
                
            }
            if(ModifiedPropertyCount>0|| m_clearUnusedProperties) 
                UpdateMaterials(m_clearUnusedProperties);
            
            //File.Delete(AssetDatabase.GetAssetPath(m_shaderOld));
            ui_ObjectField_SelectedShader.value = m_shader;
            //Cleanup temp shader

            UpdatePropertyList(true);
        }


        private bool UpdateShader()
        {
            var shaderPath = AssetDatabase.GetAssetPath(m_shader);
            
            var modifiedProperties = m_ShaderProperties.Where(p => p.isModified).ToList();
            Debug.Log("Modifies properties:"+modifiedProperties.Count());
            
            string[] lines = System.IO.File.ReadAllLines(shaderPath);
            
            string oldShaderPath = Path.GetDirectoryName(shaderPath) +"\\"+ Path.GetFileNameWithoutExtension(shaderPath) +
                                   "_OLD_" + Path.GetExtension(shaderPath);
            System.IO.File.WriteAllLines(oldShaderPath, lines);

            bool renameError=false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(" \"JSONnodeData\": \"{\\n    \\\"m_Guid\\\"") > 0) //SG previous to 10.0
                {
                    Debug.Log("Property Found1");
                    foreach (var property in modifiedProperties)
                    {
                        string propertyReference = property.isToggle ? property.Reference + "_ON" : property.Reference;
                        string propertyNewReference =
                            property.isToggle ? property.NewReference + "_ON" : property.NewReference;

                        if (lines[i].IndexOf("\\\"" + propertyReference + "\\\"") > 0)
                        {
                            //Setting override Reference
                            if (property.referenceModified)
                            {
                                Debug.Log("->property.referenceModified");
                                if (lines[i].IndexOf("\\\"m_OverrideReferenceName\\\": \\\"\\\"", StringComparison.Ordinal) > 0)
                                {
                                    Debug.Log("-> Found reference with default name.");
                                    lines[i] = lines[i].Replace(
                                        oldValue:"\\\"m_OverrideReferenceName\\\": \\\"\\\"",
                                        newValue:"\\\"m_OverrideReferenceName\\\": \\\""+property.NewReference+"\\\""
                                    );
                                }
                                else if (lines[i].IndexOf("\\\"m_OverrideReferenceName\\\": \\\""+propertyReference+"\\\"", StringComparison.Ordinal) > 0)
                                {
                                    lines[i] = lines[i].Replace(
                                        oldValue:"\\\"m_OverrideReferenceName\\\": \\\""+propertyReference+"\\\"",
                                        newValue:"\\\"m_OverrideReferenceName\\\": \\\""+property.NewReference+"\\\""
                                    );
                                    Debug.Log("-> Found reference with overriden name.");
                                }
                                else
                                {
                                    Debug.Log("-> Fail. line: " + lines[i]);
                                    renameError = true;
                                }

                            }
                            //Replacing Display Name
                            if (property.nameModified)
                            {
                                if (lines[i].IndexOf("\\\"m_Name\\\": \\\""+property.Name+"\\\"", StringComparison.Ordinal) > 0)
                                {
                                    Debug.Log("-> Name modified.");
                                    lines[i] = lines[i].Replace(
                                        oldValue:"\\\"m_Name\\\": \\\""+property.Name+"\\\"",
                                        newValue:"\\\"m_Name\\\": \\\""+property.NewName+"\\\""
                                    );
                                }
                                else
                                    renameError = true;
                            }
                        }
                        
                    }
                }
                //SG 10.0 and newer
                else if (lines[i].IndexOf("m_OverrideReferenceName") > 0 || lines[i].IndexOf("m_DefaultReferenceName") > 0)
                {
                    Debug.Log("Property Found2");
                    foreach (var property in modifiedProperties)
                    {
                        string propertyReference = property.isToggle ? property.Reference + "_ON" : property.Reference;
                        string propertyNewReference =
                            property.isToggle ? property.NewReference + "_ON" : property.NewReference;
                        if (lines[i].IndexOf("\"" + propertyReference + "\"") > 0)
                        {
                            Debug.Log("Property Found:" + propertyReference);
                            if (lines[i].IndexOf("m_DefaultReferenceName") > 0)
                            {
                                //Setting override Reference
                                if (property.referenceModified)
                                {
                                    Debug.Log("->property.referenceModified");
                                    if (lines[i + 1].IndexOf("\"\",", StringComparison.Ordinal) > 0)
                                    {
                                        lines[i + 1] = lines[i + 1].Replace("\"\",",
                                            "\"" + propertyNewReference + "\",");
                                        Debug.Log("-> Reference modified.");
                                    }
                                    else if (lines[i + 1].IndexOf("\"" + propertyReference + "\"",
                                                 StringComparison.CurrentCulture) > 0)
                                    {
                                        lines[i + 1] = lines[i + 1].Replace("\"" + propertyReference + "\"",
                                            "\"" + propertyNewReference + "\"");
                                        Debug.Log("-> Reference modified.");
                                    }
                                    else
                                    {
                                        Debug.Log("-> Fail. line: " + lines[i + 1]);
                                        renameError = true;
                                    }

                                }

                                //Replacing Display Name
                                if (property.nameModified)
                                {
                                    if (lines[i - 1].IndexOf(property.Name) > 0)
                                    {
                                        Debug.Log("-> Name modified.");
                                        lines[i - 1] = lines[i - 1].Replace(property.Name, property.NewName);
                                    }
                                    else
                                        renameError = true;
                                }
                            }
                            else if (lines[i].IndexOf("m_OverrideReferenceName") > 0)
                            {
                                //Replacing Reference
                                if (property.referenceModified)
                                {
                                    lines[i] = lines[i].Replace("\"" + propertyReference + "\"",
                                        "\"" + propertyNewReference + "\"");
                                    Debug.Log("-> Reference modified.");
                                }

                                //Replacing Display Name
                                if (property.nameModified)
                                {
                                    if (lines[i - 2].IndexOf(property.Name) > 0)
                                    {
                                        //Debug.Log("-> Name modified.");
                                        lines[i - 2] = lines[i - 2].Replace(property.Name, property.NewName);
                                    }
                                    else
                                        renameError = true;
                                }
                            }
                            else
                            {
                                renameError = true;
                                Debug.Log("Property Reference not found in ShaderGraph file: " + propertyReference);
                            }

                            modifiedProperties.Remove(property);
                            break;
                        }
                    }
                }
                
                EditorUtility.DisplayProgressBar("Patching up .ShaderGraph file...", "Patching up .ShaderGraph file...", i/(float)lines.Length);
                if (m_ShaderProperties.Count == 0 || renameError)
                    break;
            }

            if (renameError)
            {
                EditorUtility.DisplayDialog("Error while editing shadergraph file",
                    "An error occured while editing the .shadergraph file.\nThe Operation has been cancelled.","Ok");
                Debug.Log("[ShaderGraphPropertyRename]<color=red> Error:</color> Some property could not be found in the shadergraph file. It is probable that the file was modified externally, or that the shadergraph file layout has been modified.");
                return false;
            }
            else
            {
                Debug.Log("[ShaderGraphPropertyRename] Shader '"+m_shader.name+"' patched",m_shader);
            }
            
            
//            AssetDatabase.RenameAsset(shaderPath, Path.GetFileNameWithoutExtension(shaderPath) +
//                                                  "_OLD_" + Path.GetExtension(shaderPath));
            
            //Debug.Log("NewPath="+oldShaderPath);
            System.IO.File.WriteAllLines(shaderPath, lines);

            AssetDatabase.ImportAsset(shaderPath);
            AssetDatabase.ImportAsset(oldShaderPath);
            m_shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            m_shaderOld = AssetDatabase.LoadAssetAtPath<Shader>(oldShaderPath);
            //Debug.Log("[ShaderGraphPropertyRename] Shader patched.",m_shader);

            return true;
        }
        
        
        private void UpdateMaterials(bool clearAllUnusedProperties)
        {
            AssetDatabase.StartAssetEditing();
            //Init list of properties to clear
            HashSet<string>modifiedFloatProperties=new HashSet<string>();
            HashSet<string>modifiedColorProperties=new HashSet<string>();
            HashSet<string>modifiedTextureProperties=new HashSet<string>();
            if (ModifiedPropertyCount > 0)
            {
                //Patch property names in materials, if some properties have been modified.

                MaterialUpgrader matUpgrader=new MaterialUpgrader();
                var modifiedProperties = m_ShaderProperties.Where(p => p.isModified == true).ToList();
                for (int i=0;i<modifiedProperties.Count();i++)
                {
                    var prop = modifiedProperties[i];
                    var isPropertyReplace = modifiedProperties.Any(p =>
                        p.NewReference == prop.Reference && p.propertyType == prop.propertyType);

                    switch (prop.propertyType)
                    {
                        case UnityEngine.Rendering.ShaderPropertyType.Color:
                        case UnityEngine.Rendering.ShaderPropertyType.Vector:
                            if(!isPropertyReplace)
                                modifiedColorProperties.Add(new string(prop.Reference.ToCharArray()));
                            matUpgrader.RenameColor(prop.Reference,prop.NewReference);
                            break;
                        case UnityEngine.Rendering.ShaderPropertyType.Float:
                        case UnityEngine.Rendering.ShaderPropertyType.Range:
                            if(!isPropertyReplace)
                                modifiedFloatProperties.Add(prop.Reference);
                            matUpgrader.RenameFloat(prop.Reference,prop.NewReference);
                            break;
                        case UnityEngine.Rendering.ShaderPropertyType.Texture:
                            if(!isPropertyReplace)
                                modifiedTextureProperties.Add(prop.Reference);
                            matUpgrader.RenameTexture(prop.Reference,prop.NewReference);
                            break;
                    }
                }
                matUpgrader.RenameShader(m_shaderOld.name,m_shader.name);

                for (int i = 0; i < m_MaterialList.Count; i++)
                {
                    
                    
                    var material = m_MaterialList[i];
                    material.shader = m_shaderOld;
//                    Dictionary<string,float> floatValue=new Dictionary<string, float>();
//                    Dictionary<string,Color> colorValue=new Dictionary<string, Color>();
//                    Dictionary<string,Texture> textureValue=new Dictionary<string, Texture>();
//                    foreach (var property in modifiedProperties)
//                    {
//                        switch (property.propertyType)
//                        {
//                            case UnityEngine.Rendering.ShaderPropertyType.Color:
//                            case UnityEngine.Rendering.ShaderPropertyType.Vector:
//                                colorValue.Add(property.Reference,m_MaterialList[i].GetColor(property.Reference));
//                                break;
//                            case UnityEngine.Rendering.ShaderPropertyType.Float:
//                            case UnityEngine.Rendering.ShaderPropertyType.Range:
//                                floatValue.Add(property.Reference,m_MaterialList[i].GetFloat(property.Reference));
//                                break;
//                            case UnityEngine.Rendering.ShaderPropertyType.Texture:
//                                textureValue.Add(property.Reference,m_MaterialList[i].GetTexture(property.Reference));
//                                break;
//                        }
//                    }

                    MaterialUpgrader.Upgrade(material,matUpgrader,MaterialUpgrader.UpgradeFlags.None);
                    //HDShaderUtils.ResetMaterialKeywords(material); //Commented to remove dependency to HDRP. Probably not necessary, we should expect materials keyword to stay identical.
                    foreach (var property in modifiedProperties)
                    {
//                        //Restore property value (In case of property name replace)
//                        switch (property.propertyType)
//                        {
//                            
//                            case UnityEngine.Rendering.ShaderPropertyType.Color:
//                            case UnityEngine.Rendering.ShaderPropertyType.Vector:
//                                Debug.Log("Set Property "+property.NewReference+" to value "+colorValue[property.Reference]);
//                                material.SetColor(property.NewReference,colorValue[property.Reference]);
//                                break;
//                            case UnityEngine.Rendering.ShaderPropertyType.Float:
//                            case UnityEngine.Rendering.ShaderPropertyType.Range:
//                                Debug.Log("Set Property "+property.NewReference+" to value "+floatValue[property.Reference]+" OldValue="+material.GetFloat(property.NewReference));
//                                material.SetFloat(property.NewReference,floatValue[property.Reference]);
//                                break;
//                            case UnityEngine.Rendering.ShaderPropertyType.Texture:
//                                material.SetTexture(property.NewReference,textureValue[property.Reference]);
//                                break;
//                        }
                        
                        //Fix material keywords
                        if (property.isEnumKeyword) //EnumKeyword
                        {
                            //Debug.Log("RemoveKeyword:"+property.Reference+"_"+property.keywordValues[Convert.ToInt32(material.GetFloat(property.NewReference))]);
                            //Debug.Log("AddKeyword:"+property.NewReference+"_"+property.keywordValues[Convert.ToInt32(material.GetFloat(property.NewReference))]);
                            //Remove old keyword
                            material.DisableKeyword(property.Reference.ToUpper()+"_"+property.keywordValues[Convert.ToInt32(material.GetFloat(property.NewReference))]);
                            //Remove default value keyword that gets added automatically for some reason. Maybe by the material upgrader ?
                            material.DisableKeyword(property.NewReference.ToUpper()+"_"+property.keywordValues[Convert.ToInt32(material.shader.GetPropertyDefaultFloatValue(material.shader.FindPropertyIndex(property.NewReference.ToUpper())))]);
                            //Enable the correct value.
                            material.EnableKeyword(property.NewReference.ToUpper()+"_"+property.keywordValues[Convert.ToInt32(material.GetFloat(property.NewReference))]);
                        }
                        else if (property.isToggle)
                        {
                            if (material.GetFloat(property.NewReference) == 1)
                            {
                                material.DisableKeyword(property.Reference.ToUpper()+"_ON");
                                material.EnableKeyword(property.NewReference.ToUpper()+"_ON");
                            }
                            
                        }
                    }
                    
                    Debug.Log("[ShaderGraphPropertyRename] Material '"+material.name+"' patched",material);
                    EditorUtility.SetDirty(material);
                    EditorUtility.DisplayProgressBar("Patching materials", string.Format("Patching material {0}/{1}:{2}",i,m_MaterialCount,material.name), (float)i/(float)m_MaterialCount);
                }
            }
            
            if (clearAllUnusedProperties)
            {
                HashSet<string>AllUsedFloatProperties=new HashSet<string>();
                HashSet<string>AllUsedColorProperties=new HashSet<string>();
                HashSet<string>AllUsedTextureProperties=new HashSet<string>();
                for (int i = 0; i < m_shader.GetPropertyCount(); i++)
                {
                    //Debug.Log(m_shader.GetPropertyName(i)+" - "+m_shader.GetPropertyType(i));
                    switch (m_shader.GetPropertyType(i))
                    {
                        case UnityEngine.Rendering.ShaderPropertyType.Float:
                        case UnityEngine.Rendering.ShaderPropertyType.Range:
                            AllUsedFloatProperties.Add(m_shader.GetPropertyName(i));
                            break;
                        case UnityEngine.Rendering.ShaderPropertyType.Color:
                        case UnityEngine.Rendering.ShaderPropertyType.Vector:
                            AllUsedColorProperties.Add(m_shader.GetPropertyName(i));
                            break;
                        case UnityEngine.Rendering.ShaderPropertyType.Texture:
                            AllUsedTextureProperties.Add(m_shader.GetPropertyName(i));
                            break;
                    }
                }

                for (int i = 0; i < m_MaterialList.Count; i++)
                {
                    ClearUnusedProperties(m_MaterialList[i],AllUsedFloatProperties,AllUsedColorProperties,AllUsedTextureProperties);
                    EditorUtility.DisplayProgressBar("Patching materials", string.Format("Removing all unused properties {0}/{1}:{2}",i,m_MaterialCount,m_MaterialList[i].name), (float)i/(float)m_MaterialCount);

                }
            }
            else if (ModifiedPropertyCount > 0)
            {
                for (int i = 0; i < m_MaterialList.Count; i++)
                {
                    ClearUnusedProperties(m_MaterialList[i],modifiedFloatProperties,modifiedColorProperties,modifiedTextureProperties,true);
                    EditorUtility.DisplayProgressBar("Patching materials", string.Format("Removing old properties {0}/{1}:{2}",i,m_MaterialCount,m_MaterialList[i].name), (float)i/(float)m_MaterialCount);
                }
            }
            if(ModifiedPropertyCount>0)
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(m_shaderOld));
            
            //Debug.Log("[ShaderGraphPropertyRename] "+m_MaterialList.Count+" Materials patched.");
            
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
           
        }

        private void PrintPropertyValues(Material material,int count,bool prePatch=false)
        {
            Debug.Log("Material " + material.name + " - " +count);
            var modifiedProperties = m_ShaderProperties.Where(p => p.isModified == true).ToList();
            foreach (var property in modifiedProperties)
            {
                //Restore property value (In case of property name replace)
                switch (property.propertyType)
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        Debug.Log("Property " + property.NewReference + " =" +
                                  material.GetFloat(prePatch?property.Reference:property.NewReference));
                        break;
                }
            }
        }

        private void ClearUnusedProperties(Material material, HashSet<string> floatProperties, HashSet<string> colorProperties, HashSet<string> textureProperties,bool cleanRenamedOnly=false)
        {
            SerializedObject serializedMaterial=new SerializedObject(material);
            SerializedProperty savedFloats = serializedMaterial.FindProperty("m_SavedProperties.m_Floats");
            for (int i = savedFloats.arraySize-1; i >=0; i--)
            {
                

                var currentProperty = savedFloats.GetArrayElementAtIndex(i);
                var currentPropertyReference = currentProperty.FindPropertyRelative("first").stringValue;
                if (floatProperties.Contains(currentPropertyReference))
                {
                    if (cleanRenamedOnly)
                    {
                        savedFloats.DeleteArrayElementAtIndex(i);
                    }
                    continue;
                }
                if(!cleanRenamedOnly)
                    savedFloats.DeleteArrayElementAtIndex(i);  
                
            }
            SerializedProperty savedColors = serializedMaterial.FindProperty("m_SavedProperties.m_Colors");
            for (int i = savedColors.arraySize-1; i >=0; i--)
            {
                var currentProperty = savedColors.GetArrayElementAtIndex(i);
                var currentPropertyReference = currentProperty.FindPropertyRelative("first").stringValue;
                if (colorProperties.Contains(currentPropertyReference))
                {
                    if(cleanRenamedOnly)
                        savedFloats.DeleteArrayElementAtIndex(i);
                    continue;
                }
                if(!cleanRenamedOnly)
                    savedColors.DeleteArrayElementAtIndex(i);
            }
            SerializedProperty savedTextures = serializedMaterial.FindProperty("m_SavedProperties.m_TexEnvs");
            for (int i = savedTextures.arraySize-1; i >=0; i--)
            {
                var currentProperty = savedTextures.GetArrayElementAtIndex(i);
                var currentPropertyReference = currentProperty.FindPropertyRelative("first").stringValue;
                if (textureProperties.Contains(currentPropertyReference))
                {
                    if(cleanRenamedOnly)
                        savedFloats.DeleteArrayElementAtIndex(i);
                    continue;
                }
                if(!cleanRenamedOnly)
                    savedTextures.DeleteArrayElementAtIndex(i);
            }
            serializedMaterial.ApplyModifiedProperties();
        }


        private void UpdateVCSStatus(bool waitForResult=false)
        {
            if (!Provider.enabled || !Provider.isActive)
                return;
            
            //shaderVCSTask.
            List<string> assetPaths=new List<string>();
            assetPaths.Add(AssetDatabase.GetAssetPath(m_shader));
            Dictionary<string, Material> materialPathDict=new Dictionary<string, Material>();
            foreach (var material in m_MaterialList)
            {
                string path = AssetDatabase.GetAssetPath(material);
                assetPaths.Add(path);
                materialPathDict[path] = material;
            }

            
            m_VersionControlUpdate=Provider.Status(assetPaths.ToArray());

            if (waitForResult)
            {
                EditorUtility.DisplayProgressBar("VersionControl Check", "Checking version control status of shader and materials.", 0);
                m_VersionControlUpdate.Wait();
                EditorUtility.ClearProgressBar();
            }
        }
        


        private bool CheckConflict() //Returns true if no problem
        {

            UpdateVCSStatus(true);
            
            if (!Provider.enabled || !Provider.isActive)
                return true;
            
            if (!CheckRemoteLock())
                return false;

            var remoteCheckedOutAssetList = m_VersionControlUpdate.assetList.Where(p => p.IsOneOfStates(new[]{
                Asset.States.CheckedOutRemote})).ToList();
            
            var problemAssetList = m_VersionControlUpdate.assetList.Where(p => p.IsOneOfStates(new[]
            {
                Asset.States.OutOfSync, Asset.States.LockedRemote, Asset.States.MovedRemote, Asset.States.Conflicted
            })).ToList();

            if (remoteCheckedOutAssetList.Count > 0 && problemAssetList.Count == 0)
            {
                var warningPrompt=!EditorUtility.DisplayDialog("Version Control Warning",
                    "Some of the materials or shader assets are checked out remotely.\n" +
                    "You can find those with the File List window."
                    , "Proceed anyway","Cancel");
                if (warningPrompt)
                {
                    Debug.Log("[ShaderGraphPropertyRename] Operation canceled by the user");
                    return false;
                }
            }
            else if (problemAssetList.Count != 0)
            {
                var warningPrompt=!EditorUtility.DisplayDialog("Version Control Warning",
                    "Some of the materials assets are: Out of sync, conflicted, or remotely locked.\n" +
                    "You can find those with the File List window.\n" +
                    "It is highly recommended to fix those issues before proceeding", "Proceed anyway","Cancel");
                if (warningPrompt)
                {
                    Debug.Log("[ShaderGraphPropertyRename] Operation canceled by the user");
                    return false;
                }
            }
            
            return true;
        }

        public void UpdateMaterialList(bool remote=false)
        {
            if (m_shader == null)
            {
                MaterialCount = 0;
                return;
            }

            if(m_MaterialList==null)
                m_MaterialList=new List<Material>();
            else
                m_MaterialList.Clear();
            
            var allMaterialGUIDs = AssetDatabase.FindAssets("t:Material");
            if (allMaterialGUIDs == null || allMaterialGUIDs.Length==0)
                throw new Exception("No materials loaded");
            
            foreach (var materialGUID in allMaterialGUIDs)
            {
                Material currentMaterial = null;
                Shader currentShader = null;
                
                var materialPath = AssetDatabase.GUIDToAssetPath(materialGUID);
                if (materialPath == null)
                    continue;
                
                if (Path.GetExtension(materialPath).ToLower() == ".fbx") //Skip FBX materials
                {
                    continue;
                }
                
                currentMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (currentMaterial == null)
                    continue;
                currentShader = currentMaterial.shader;

                if (!currentShader.hideFlags.HasFlag(HideFlags.HideInInspector) && currentShader==m_shader)
                    m_MaterialList.Add(currentMaterial);
            }
            MaterialCount = m_MaterialList.Count;
            UpdateVCSStatus(true);
            onUpdateMaterialList?.Invoke(this, EventArgs.Empty);
        }
        
        public bool CheckOutAllFiles(bool displayPrompt=false)
        {
            if (m_VersionControlUpdate==null || m_VersionControlUpdate.assetList == null || m_VersionControlUpdate.assetList.Count == 0)
                return true;
            
            if (!CheckRemoteLock())
                return false;
            if (displayPrompt)
            {
                if (!CheckConflict())
                    return false;
                var warningPrompt=!EditorUtility.DisplayDialog("Confirm Checkout",
                    m_VersionControlUpdate.assetList.Count/2+" assets will be checked out. Proceed ?", "CheckOut Files","Cancel");
                if (warningPrompt)
                {
                    Debug.Log("[ShaderGraphPropertyRename] Check Out canceled by the user");
                    return false;
                }
            }
            UpdateMaterialList();

            //Look if changeset already exists
            ChangeSet destChangeset=null;
            destChangeset = GetChangeset("ShaderGraphPropertyRename - " + m_shader.name);
            if (destChangeset == null)//Create changeset if it doesn't exists
            {
                var createChangeSetTask = Provider.Submit(null, null, "ShaderGraphPropertyRename - "+m_shader.name, true);
                createChangeSetTask.Wait();
            }
            destChangeset = GetChangeset("ShaderGraphPropertyRename - " + m_shader.name);
            
            //Checking out files
            var taskCheckout=Provider.Checkout(m_VersionControlUpdate.assetList,CheckoutMode.Both,destChangeset);
            EditorUtility.DisplayProgressBar("Version Control Progress", "Checking Out files...", 0);
            taskCheckout.Wait();
            
            //Move assets to changeset (In case they were already checked out)
            var taskMove=Provider.ChangeSetMove(m_VersionControlUpdate.assetList,destChangeset);
            EditorUtility.DisplayProgressBar("Version Control Progress", "Checking Out files...", 0);
            taskMove.Wait();
            
            
            EditorUtility.ClearProgressBar();
            UpdateMaterialList();
            return true;
        }
        
        public bool LockAllFiles(bool displayPrompt=false)
        {
            if (m_VersionControlUpdate==null || m_VersionControlUpdate.assetList == null || m_VersionControlUpdate.assetList.Count == 0)
                return true;
            
            if (!CheckRemoteLock())
                return false;
            if (displayPrompt)
            {
                if (!CheckConflict())
                    return false;
                var warningPrompt=!EditorUtility.DisplayDialog("Confirm Lock",
                    m_VersionControlUpdate.assetList.Count/2+" assets will be locked. Proceed ?", "Lock Files","Cancel");
                if (warningPrompt)
                {
                    Debug.Log("[ShaderGraphPropertyRename] File Lock canceled by the user");
                    return false;
                }
            }

            if (!CheckOutAllFiles(false))
                return false;
            
            bool isLockValid=Provider.LockIsValid(m_VersionControlUpdate.assetList);
            if (!isLockValid)
            {
                EditorUtility.DisplayDialog("Lock Failed",
                    "The files could not be locked, check if any file is locked remotely.", "Ok");
                return false;
            }
            var task=Provider.Lock(m_VersionControlUpdate.assetList,true);
            EditorUtility.DisplayProgressBar("Version Control Progress", "Locking files...", 0);
            task.Wait();
            
            EditorUtility.ClearProgressBar();
            UpdateMaterialList();
            return true;
        }

        private bool CheckRemoteLock()
        {
            if (!Provider.enabled || !Provider.isActive)
                return true;

            if (m_VersionControlUpdate==null || m_VersionControlUpdate.assetList == null || m_VersionControlUpdate.assetList.Count == 0)
                return true;
            
            if (m_VersionControlUpdate.assetList.Any(p => p.IsState(Asset.States.LockedRemote)))
            {
                var warningPrompt=!EditorUtility.DisplayDialog("Remove Lock",
                    "Some of the assets are locked remotely, the operation is therefor canceled.\n" +
                    "You can find those files with the File List window.\n" +
                    "This issue needs to be fixed to proceed.", "Cancel");
                Debug.Log("[ShaderGraphPropertyRename] Operation cancelled: Some assets are locked remotely.");
                return false;
            }

            return true;
        }

        private ChangeSet GetChangeset(string changeSetName)
        {
            var getChangeSetsTask = Provider.ChangeSets();
            getChangeSetsTask.Wait();
            foreach (var queriedChangeSet in getChangeSetsTask.changeSets)
            {
                //Debug.Log(" CL "+queriedChangeSet.id+" - "+queriedChangeSet.description);
                if (queriedChangeSet.description.Contains(changeSetName))
                {
                    return queriedChangeSet;
                }
            }

            return null;
        }

        private void OnFocus()
        {
            CheckVersionControlStatus();
        }

        public void CheckVersionControlStatus(bool remote=false)
        {
            if (!initiated || ui_Button_Checkout==null)
                return;
            
            if (!Provider.enabled || !Provider.isActive)
            {
                ui_Button_Checkout.SetEnabled(false);
                ui_Button_Lock.SetEnabled(false);
                ui_Button_Checkout.tooltip = ui_Button_Lock.tooltip = "Version Control is currently disabled or disconnected";
            }
            else
            {
                ui_Button_Checkout.SetEnabled(Provider.hasCheckoutSupport);
                ui_Button_Checkout.tooltip = Provider.hasCheckoutSupport ? 
                    "Check Out all the affected files (The selected shader and all the materials using it)" 
                    : "The current version control does not support file checkout";

                ui_Button_Lock.SetEnabled(m_HasLockingSupport);
                ui_Button_Lock.tooltip = m_HasLockingSupport ? 
                        "Lock all the affected files (The selected shader and all the materials using it)" 
                        : "File locking is not supported by the current Version Control, or by this Unity version (Supported with 2020.2 or newer).";
                
            }
            if(m_affectedFileWindow!=null && !remote)
                m_affectedFileWindow.CheckVersionControlStatus(true);
        }
        
        public class BoolEventArg : EventArgs
        {
            public bool boolValue { get; set; }

            public BoolEventArg(bool value)
            {
                boolValue = value;
            }
        }
        
       
    }


}