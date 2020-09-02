### **_ShaderGraph Property Renamer_**

*Adds a new window accessible in the menu Tools>ShaderGraph Property renamer*
*This tool allows the user to change reference names and display name of shader graph properties.*

*An additional window allows the user to see all files that would be affected by the change (So, the shader and every material using this shader) as well as their version control status*
*Some buttons allows the user to CheckOut and/or lock all the files*
*If some files are locked remotely by another user the operation will be canceled.*

*The following operations are done when the changes are applied:*
* The ShaderGraph file will be patched, replacing the properties reference names/display names.
* All the materials using the modified shadergraph will be patched.
* Optionally all properties stored in the material, that are not present in the shader, can be cleaned up.
* Keywords (Boolean and Enum) also get corrected in every materials.

*![ShaderGraph Property Renamer Window.](Documentation/images/Picture.png)*