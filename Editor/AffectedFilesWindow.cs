
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using System.Linq;
using UnityEditor.UIElements;
using UnityEditor.VersionControl;
using Object = UnityEngine.Object;

namespace ShaderGraphPropertyRenamer
{
    [Serializable]
    public class AffectedFilesWindow : EditorWindow
    {
        private VisualElement root;
        private VisualElement ui_FileListContainer;
        private Label ui_Label_TotalFileCount;
        private Button ui_Button_Refresh;
        private Button ui_Button_Checkout;
        private Button ui_Button_Lock;

        static AffectedFilesWindow m_Window;
        private ShaderGraphPropertyRenamerWindow m_renamerWindow;
        
        private List<Object> m_ObjectList;
        private List<Asset> m_AssetList;
        private ListView m_ListView;
        private bool m_HasLockingSupport = false;

        private bool m_initiated=false;
        public static AffectedFilesWindow Instance;
        public bool IsOpen
        {
            get { return Instance != null; }
        }
        
        public static void ShowWindow()
        {
            GetWindow<AffectedFilesWindow>(false, "Affected Files", true);
            //m_Window.UpdateWindow(true);
        }


        private void OnEnable()
        {
            Instance = this;
            if(m_ObjectList==null)
                m_ObjectList=new List<Object>();
            if(m_AssetList==null)
                m_AssetList=new List<Asset>();
            this.titleContent=new GUIContent("Affected Files");
            this.minSize = new Vector2(415, 350);
            string scriptPath = Utility.GetScriptPath("AffectedFilesWindow");
            VisualTreeAsset uiAsset =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(scriptPath + "AffectedFilesWindow.uxml");
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(scriptPath + "ShaderGraphPropertyRenamerWindow.uss");
            VisualElement ui = uiAsset.CloneTree();
            ui.style.flexGrow = 1f;
            
            Type providerType = typeof(Provider);
            m_HasLockingSupport = providerType.GetProperty("hasLockingSupport") != null;
            if (m_HasLockingSupport)
                m_HasLockingSupport = (bool) typeof(Provider).GetProperty("hasLockingSupport").GetValue(null);
            
            root = rootVisualElement;
            root.style.flexGrow = 1;
            root.Add(ui);
            root.styleSheets.Add(uss);
            InitUI();
            CheckVersionControlStatus();
        }

        

        private void OnDisable()
        {
            m_renamerWindow.onUpdateMaterialList -= UpdateFileListEvent;
        }



        public void InitUI()
        {
            ui_FileListContainer= root.Query<VisualElement>("FileListContainer").First();
            ui_Label_TotalFileCount= root.Query<Label>("Label_TotalFileCount").First();
            
            ui_Button_Refresh= root.Query<Button>("Button_Refresh").First();
            ui_Button_Refresh.clicked += () => { m_renamerWindow.UpdateMaterialList();};
            
            ui_Button_Checkout= root.Query<Button>("Button_Checkout").First();
            ui_Button_Checkout.clicked += () =>
            {
                m_renamerWindow.CheckOutAllFiles(true);
            };

            ui_Button_Lock= root.Query<Button>("Button_Lock").First();
            ui_Button_Lock.clicked += () =>
            {
                m_renamerWindow.LockAllFiles(true);
            };
            if (!(Provider.isActive && Provider.enabled))
            {
                ui_Button_Checkout.SetEnabled(false);
                ui_Button_Lock.SetEnabled(false);
                ui_Button_Checkout.tooltip =
                    "Version Control Disabled. Enable it in the Project Settings and re-open this window.";
                ui_Button_Lock.tooltip =
                    "Version Control Disabled. Enable it in the Project Settings and re-open this window.";
            }
            
            //CreateListView
            Func<VisualElement> makeItem = () => new VisualElement();
            //ListView item update
            Action<VisualElement, int> bindItem = (e, i) =>
            {
                e.Clear();
                e.userData = i;
                e.AddToClassList("fileListElement");
 
                if (m_AssetList.Count > i)
                {
                    bool isWarning = (m_AssetList[i].IsState(Asset.States.Conflicted)
                                      || m_AssetList[i].IsState(Asset.States.LockedRemote)
                                      || m_AssetList[i].IsState(Asset.States.MovedRemote)
                                      || m_AssetList[i].IsState(Asset.States.OutOfSync));
                    e.EnableInClassList("warning",isWarning);
                    if (isWarning)
                    {
                    
                        string toolTipString = (m_AssetList[i].IsState(Asset.States.OutOfSync)?"Out Of Sync, ":"")
                                               +(m_AssetList[i].IsState(Asset.States.Conflicted)?"Conflicted, ":"")
                                               +(m_AssetList[i].IsState(Asset.States.LockedRemote)?"Locked remotely, ":"")
                                               +(m_AssetList[i].IsState(Asset.States.MovedRemote)?"Moved remotely, ":"");
                        e.tooltip = toolTipString.Substring(0, toolTipString.Length - 2);
                    }
                }

                
                    
                //UnityEditorInternal.VersionControl.Overlay.DrawOverlay();
                var icon = new IMGUIContainer(() =>
                {
                    //Debug.Log("i="+i+" - ObjectList Count="+m_ObjectList.Count+" - AssetListCount="+m_AssetList.Count);
                    if (m_ObjectList.Count <= i)
                        return;
                    //Debug.Log("i="+i+" - ObjectList Count="+m_ObjectList.Count+" - AssetListCount="+m_AssetList.Count);
                    string assetPath = AssetDatabase.GetAssetPath(m_ObjectList[i]);
                    GUI.DrawTexture(new Rect(7, 0, 16, 16), AssetDatabase.GetCachedIcon(assetPath), ScaleMode.ScaleToFit, true);
                    if (m_AssetList.Count > i)
                    {
                        UnityEditorInternal.VersionControl.Overlay.DrawOverlay(m_AssetList[i],new Rect(0, 0, 16, 16));
                    }
                    //Debug.Log("i="+i+" - ObjectList Count="+m_ObjectList.Count+" - AssetListCount="+m_AssetList.Count+"[END]");

                });
                icon.AddToClassList("fileIcon");
                e.Add(icon);
                e.Add(new Label(m_ObjectList[i].name));
                VisualElement warningIcon=new VisualElement();
                warningIcon.AddToClassList("duplicateWarning");
                e.Add(warningIcon);

            };
            // Provide the list view with an explict height for every row
            // so it can calculate how many items to actually display
            const int itemHeight = 20;

            
            m_ListView=new ListView(m_ObjectList, itemHeight, makeItem, bindItem);
            m_ListView.selectionType = SelectionType.Multiple;
            m_ListView.onItemsChosen += obj => Selection.activeObject=(Object)obj.First();
            m_ListView.onSelectionChange += objects =>
            {
                List<Object> selectionList=(from obj in objects select obj as Object).ToList();
                
                Selection.objects = selectionList.ToArray();
            };
            m_ListView.style.flexGrow = 1.0f;
            ui_FileListContainer.Add(m_ListView);

        }

        

        public void Init(ShaderGraphPropertyRenamerWindow renamerWindow)
        {
            m_renamerWindow = renamerWindow;
            if(m_initiated)
                m_renamerWindow.onUpdateMaterialList -= UpdateFileListEvent;
            m_renamerWindow.onUpdateMaterialList += UpdateFileListEvent;
            m_initiated = true;
            UpdateFileList();
        }

        private void UpdateFileListEvent(object sender, EventArgs args)
        {
            UpdateFileList();
        }
        private void UpdateFileList()
        {
            m_ObjectList.Clear();
            m_AssetList.Clear();

            if (m_renamerWindow.Shader != null)
            {
                m_ObjectList.Add(m_renamerWindow.Shader);
                m_ObjectList.InsertRange(1,m_renamerWindow.m_MaterialList.OrderBy(e=>e.name).ToArray());
                var updateTask = m_renamerWindow.m_VersionControlUpdate;
//                if(updateTask.assetList==null || updateTask.assetList.Count==0)
//                    updateTask.Wait();
                if (updateTask != null && updateTask.assetList != null)
                {
                    if(updateTask.assetList.Count==0)
                        updateTask.Wait();
                    if (updateTask.assetList.Any(p =>
                        p.path.IndexOf(".shadergraph", StringComparison.CurrentCultureIgnoreCase) == -1))
                    {
                        m_AssetList.AddRange(updateTask.assetList.Where(p=>!p.isMeta&& p.path.IndexOf(".shadergraph",StringComparison.CurrentCultureIgnoreCase)==-1));
                        m_AssetList = m_AssetList.OrderBy(p => p.name).ToList();
                    }
                    m_AssetList.Insert(0,updateTask.assetList.First(p => !p.isMeta&& p.path.IndexOf(".shadergraph",StringComparison.CurrentCultureIgnoreCase)!=-1));
                }
            }
            

            ui_Label_TotalFileCount.text = m_AssetList.Count.ToString();
            m_ListView.itemsSource = m_ObjectList;
            m_ListView.Refresh();
        }
        
        private void OnFocus()
        {
            if (m_initiated)
            {
                UpdateFileList();
            }
           
        }

        public void CheckVersionControlStatus(bool remote=false)
        {
            if (!Provider.enabled || !Provider.isActive)
            {
                ui_Button_Checkout.SetEnabled(false);
                ui_Button_Lock.SetEnabled(false);
                ui_Button_Checkout.tooltip =
                    ui_Button_Lock.tooltip = "Version Control is currently disabled or disconnected";
            }
            else
            {
                ui_Button_Checkout.SetEnabled(Provider.hasCheckoutSupport);
                ui_Button_Checkout.tooltip = Provider.hasCheckoutSupport
                    ? "Check Out all the affected files (The selected shader and all the materials using it)"
                    : "The current version control does not support file checkout";
                
                ui_Button_Lock.SetEnabled(m_HasLockingSupport);
                ui_Button_Lock.tooltip = m_HasLockingSupport ? 
                    "Lock all the affected files (The selected shader and all the materials using it)" 
                    : "File locking is not supported by the current Version Control, or by this Unity version (Supported with 2020.2 or newer).";
            }

            if (m_renamerWindow != null && !remote)
            {
                m_renamerWindow.CheckVersionControlStatus(true);
            }
        }
    }


}