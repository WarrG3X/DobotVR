﻿//---------------------------------//
//  This file is part of MuJoCo    //
//  Written by Emo Todorov         //
//  Copyright (C) 2018 Roboti LLC  //
//---------------------------------//

#if UNITY_EDITOR

using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEditor;


public class MJImport : EditorWindow 
{

    // script options
    public string modelFile = "<enter file or browse>";
    public string fileName = "";
    public bool recomputeUV = false;
    public bool recomputeNormal = false;
    public bool importTexture = true;
    public bool enableRemote = false;
    public string tcpAddress = "127.0.0.1";
    public int tcpPort = 1050;
    public bool noVSync = false;

    // Unity element arrays
    Texture2D[] textures;
    Material[] materials;
    GameObject[] objects;
    GameObject root = null;


    // create menu item
    [MenuItem("Window/MuJoCo Import")]
    public static void ShowWindow()
    {
        // show existing window instance, or make one
        EditorWindow.GetWindow(typeof(MJImport), false, "MuJoCo Import");
    }


    // present GUI, get options, run importer
    void OnGUI()
    {
        // get file name
        EditorGUILayout.Space();
        GUILayout.BeginHorizontal();
        if( GUILayout.Button("Browse ...") )
        { 
            // get directory of current model, otherwise use Assets
            string dir = "Assets";
            int lastsep = modelFile.LastIndexOf('/');
            if( lastsep>=0 )
                dir = modelFile.Substring(0, lastsep);

            // file open dialog
            string temp = EditorUtility.OpenFilePanel("Select file", dir, "xml,urdf,mjb");
            if( temp.Length!=0 )
            {
                modelFile = temp;
                this.Repaint();
            }
        }

        modelFile = EditorGUILayout.TextField(modelFile);
        GUILayout.EndHorizontal();

        // options
        EditorGUILayout.Space();
        recomputeNormal = EditorGUILayout.Toggle("Recompute Normals", recomputeNormal);
        recomputeUV = EditorGUILayout.Toggle("Recompute UVs", recomputeUV);
        importTexture = EditorGUILayout.Toggle("Import Textures", importTexture);
        enableRemote = EditorGUILayout.Toggle("Enable Remote", enableRemote);
        using( new EditorGUI.DisabledScope(enableRemote==false) )
        {
            tcpAddress = EditorGUILayout.TextField("TCP Address", tcpAddress);
            tcpPort = EditorGUILayout.IntField("TCP Port", tcpPort);
            noVSync = EditorGUILayout.Toggle("Disable V-Sync", noVSync);
        }

        // run importer or clear
        EditorGUILayout.Space();
        GUILayout.BeginHorizontal();
        if( GUILayout.Button("Import Model", GUILayout.Height(25)) )
             RunImport();

        if( GUILayout.Button("Clear Model", GUILayout.Height(25)) )
        {
            // delete MuJoCo hierarchy
            GameObject top = GameObject.Find("MuJoCo");
            if( top )
                DestroyImmediate(top);
        }

        GUILayout.EndHorizontal();
    }


    // convert transform from plugin to GameObject
    static unsafe void SetTransform(GameObject obj, MJP.TTransform transform)
    {
        Quaternion q = new Quaternion(0, 0, 0, 1);
        q.SetLookRotation(
            new Vector3(transform.yaxis[0], -transform.yaxis[2], transform.yaxis[1]),
            new Vector3(-transform.zaxis[0], transform.zaxis[2], -transform.zaxis[1])
        );

        obj.transform.localPosition = new Vector3(-transform.position[0], transform.position[2], -transform.position[1]);
        obj.transform.localRotation = q;
        obj.transform.localScale = new Vector3(transform.scale[0], transform.scale[2], transform.scale[1]);
    }


    // convert transform from plugin to Camera
    static unsafe void SetCamera(Camera cam, MJP.TTransform transform)
    {
        Quaternion q = new Quaternion(0, 0, 0, 1);
        q.SetLookRotation(
            new Vector3(transform.zaxis[0], -transform.zaxis[2], transform.zaxis[1]),
            new Vector3(-transform.yaxis[0], transform.yaxis[2], -transform.yaxis[1])
        );

        cam.transform.localPosition = new Vector3(-transform.position[0], transform.position[2], -transform.position[1]);
        cam.transform.localRotation = q;
    }


    // make scene-speficic directories for materials and textures
    private void MakeDirectory(string parent, string directory)
    {
        // check subdirectories of parent
        string[] subdir = AssetDatabase.GetSubFolders(parent);
        bool found = false;
        string fullpath = parent + "/" + directory;
        foreach( string str in subdir )
            if( str==fullpath )
            {
                found = true;
                break;
            }

        // create if not found
        if( !found )
            AssetDatabase.CreateFolder(parent, directory);
    }


    // adjust material given object color
    private void AdjustMaterial(Material m, float r, float g, float b, float a)
    {
        // set main color, 
        m.SetColor("_Color", new Color(r, g, b, a));

        // prepare for emission (used for highlights at runtime)
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(0, 0, 0, 1));

        // set transparent mode (magic needed to convince Unity to do it)
        if( a<1 )
        {
            m.SetFloat("_Mode", 2);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
        }
    }


    // add camera
    private unsafe void AddCamera()
    {
        // add camera under root
        GameObject camobj = new GameObject("camera");
        camobj.transform.parent = root.transform;
        Camera thecamera = camobj.AddComponent<Camera>();

        // set field of view, near, far
        MJP.TCamera cam;
        MJP.GetCamera(-1, &cam);
        thecamera.fieldOfView = cam.fov;
        thecamera.nearClipPlane = cam.znear;
        thecamera.farClipPlane = cam.zfar;

        // set transform
        MJP.TTransform transform;
        MJP.GetCameraState(-1, &transform);
        SetCamera(thecamera, transform);
    }


    // import textures
    private unsafe void ImportTextures(int ntexture)
    {
        // allocate array, find existing
        textures = new Texture2D[ntexture];
        Object[] alltextures = Resources.FindObjectsOfTypeAll(typeof(Texture2D));

        // process textures
        for( int i=0; i<ntexture; i++ )
        {
            // get texture name
            StringBuilder name = new StringBuilder(100);
            MJP.GetElementName(MJP.TElement.TEXTURE, i, name, 100);
            string texname =  fileName + "_" + name.ToString();

            // get texture descriptor and save
            MJP.TTexture tex;
            MJP.GetTexture(i, &tex);

            // MuJoCo cube texture: use only top piece
            if( tex.cube>0 )
                tex.height = tex.width;

            // find existing texture
            foreach( Object texx in alltextures )
                if( texx.name==texname )
                {
                    textures[i] = (Texture2D)texx;

                    // resize if different
                    if( textures[i].width!=tex.width || textures[i].height!=tex.height )
                        textures[i].Resize(tex.width, tex.height);

                    break;
                }

            // not found: create new texture
            if( textures[i]==null )
                textures[i] = new Texture2D(tex.width, tex.height);

            // copy array
            Color32[] color = new Color32[tex.width*tex.height];
            for( int k=0; k<tex.width*tex.height; k++ )
            { 
                color[k].r = tex.rgb[3*k];
                color[k].g = tex.rgb[3*k+1];
                color[k].b = tex.rgb[3*k+2];
                color[k].a = 255;
            }

            // load data and apply
            textures[i].SetPixels32(color);
            textures[i].Apply();

            // create asset in database if not aleady there
            if( !AssetDatabase.Contains(textures[i]) )
                AssetDatabase.CreateAsset(textures[i], "Assets/Textures/" + texname + ".asset");
        }

        AssetDatabase.Refresh();
    }


    // import materials
    private unsafe void ImportMaterials(int nmaterial)
    {
        // allocate array, find all existing
        materials = new Material[nmaterial];
        Object[] allmaterials = Resources.FindObjectsOfTypeAll(typeof(Material));

        // process materials
        for( int i=0; i<nmaterial; i++ )
        {
            // get material name
            StringBuilder name = new StringBuilder(100);
            MJP.GetElementName(MJP.TElement.MATERIAL, i, name, 100);
            string matname =  fileName + "_" + name.ToString();

            // find existing material
            foreach( Object matt in allmaterials )
                if( matt!=null && matt.name==matname )
                {
                    materials[i] = (Material)matt;
                    break;
                }

            // not found: create new material
            materials[i] = new Material(Shader.Find("Standard"));

            // get material descriptor and save
            MJP.TMaterial mat;
            MJP.GetMaterial(i, &mat);

            // set properties
            materials[i].name = matname;
            materials[i].EnableKeyword("_EMISSION");
            materials[i].SetColor("_Color", new Color(mat.color[0], mat.color[1], mat.color[2], mat.color[3]));
            materials[i].SetColor("_EmissionColor", new Color(mat.emission, mat.emission, mat.emission, 1));
            if( mat.color[3]<1 )
                materials[i].SetFloat("_Mode", 3.0f);

            // set texture if present
            if( mat.texture>=0 && importTexture )
            { 
                materials[i].mainTexture = textures[mat.texture];
                materials[i].mainTextureScale = new Vector2(mat.texrepeat[0], mat.texrepeat[1]);
            }

            // create asset in database if not aleady there
            if( !AssetDatabase.Contains(materials[i]) )
                AssetDatabase.CreateAsset(materials[i], "Assets/Materials/" + matname + ".mat");
        }

        AssetDatabase.Refresh();
    }


    // import renderable objects
    private unsafe void ImportObjects(int nobject)
    {
        // make primitives
        PrimitiveType[] ptypes = {
            PrimitiveType.Plane,
            PrimitiveType.Sphere,
            PrimitiveType.Cylinder,
            PrimitiveType.Cube
        };
        GameObject[] primitives = new GameObject[4];
        for( int i=0; i<4; i++)
            primitives[i] = GameObject.CreatePrimitive(ptypes[i]);

        // allocate array
        objects = new GameObject[nobject];

        // process objects
        for( int i=0; i<nobject; i++ )
        {
            // get object name
            StringBuilder name = new StringBuilder(100);
            MJP.GetObjectName(i, name, 100);

            // create new GameObject, place under root
            objects[i] = new GameObject(name.ToString());
            objects[i].AddComponent<MeshFilter>();
            objects[i].AddComponent<MeshRenderer>();
            objects[i].transform.parent = root.transform;

            // get components
            MeshFilter filt = objects[i].GetComponent<MeshFilter>();
            MeshRenderer rend = objects[i].GetComponent<MeshRenderer>();

            // get MuJoCo object descriptor
            MJP.TObject obj;
            MJP.GetObject(i, &obj);

            // set mesh
            switch( (MJP.TGeom)obj.geomtype )
            {
                case MJP.TGeom.PLANE:
                    filt.sharedMesh = primitives[0].GetComponent<MeshFilter>().sharedMesh;
                    break;

                case MJP.TGeom.SPHERE:
                    filt.sharedMesh = primitives[1].GetComponent<MeshFilter>().sharedMesh;
                    break;

                case MJP.TGeom.CYLINDER:
                    filt.sharedMesh = primitives[2].GetComponent<MeshFilter>().sharedMesh;
                    break;

                case MJP.TGeom.BOX:
                    filt.sharedMesh = primitives[3].GetComponent<MeshFilter>().sharedMesh;
                    break;

                case MJP.TGeom.HFIELD:
                    int nrow = obj.hfield_nrow;
                    int ncol = obj.hfield_ncol;
                    int r, c;

                    // allocate
                    Vector3[] hfvertices = new Vector3[nrow*ncol + 4*nrow+4*ncol];
                    Vector2[] hfuv = new Vector2[nrow*ncol + 4*nrow+4*ncol];
                    int[] hffaces0 = new int[3*2*(nrow-1)*(ncol-1)];
                    int[] hffaces1 = new int[3*(4*(nrow-1)+4*(ncol-1))];

                    // vertices and uv: surface
                    for( r=0; r<nrow; r++ )
                        for( c=0; c<ncol; c++ )
                        {
                            int k = r*ncol+c;
                            float wc = c / (float)(ncol-1);
                            float wr = r / (float)(nrow-1);

                            hfvertices[k].Set(-(wc-0.5f), obj.hfield_data[k], -(wr-0.5f));
                            hfuv[k].Set(wc, wr);
                        }

                    // vertices and uv: front and back
                    for( r=0; r<nrow; r+=(nrow-1) )
                        for( c=0; c<ncol; c++ )
                        {
                            int k = nrow*ncol + 2*((r>0?ncol:0)+c);
                            float wc = c / (float)(ncol-1);
                            float wr = r / (float)(nrow-1);

                            hfvertices[k].Set(-(wc-0.5f), -0.5f, -(wr-0.5f));
                            hfuv[k].Set(wc, 0);
                            hfvertices[k+1].Set(-(wc-0.5f), obj.hfield_data[r*ncol+c], -(wr-0.5f));
                            hfuv[k+1].Set(wc, 1);
                        }

                    // vertices and uv: left and right
                    for( c=0; c<ncol; c+=(ncol-1) )
                        for( r=0; r<nrow; r++ )
                        {
                            int k = nrow*ncol + 4*ncol + 2*((c>0?nrow:0)+r);
                            float wc = c / (float)(ncol-1);
                            float wr = r / (float)(nrow-1);

                            hfvertices[k].Set(-(wc-0.5f), -0.5f, -(wr-0.5f));
                            hfuv[k].Set(wr, 0);
                            hfvertices[k+1].Set(-(wc-0.5f), obj.hfield_data[r*ncol+c], -(wr-0.5f));
                            hfuv[k+1].Set(wr, 1);
                        }


                    // faces: surface
                    for( r=0; r<nrow-1; r++ )
                        for( c=0; c<ncol-1; c++ )
                        {
                            int f = r*(ncol-1)+c;
                            int k = r*ncol+c;

                            // first face in rectangle
                            hffaces0[3*2*f]   = k;
                            hffaces0[3*2*f+2] = k+1;
                            hffaces0[3*2*f+1] = k+ncol+1;

                            // second face in rectangle
                            hffaces0[3*2*f+3] = k;
                            hffaces0[3*2*f+5] = k+ncol+1;
                            hffaces0[3*2*f+4] = k+ncol;
                        }

                    // faces: front and back
                    for( r=0; r<2; r++ )
                        for( c=0; c<ncol-1; c++ )
                        {
                            int f = ((r>0?(ncol-1):0)+c);
                            int k = nrow*ncol + 2*((r>0?ncol:0)+c);

                            // first face in rectangle
                            hffaces1[3*2*f]   = k;
                            hffaces1[3*2*f+2] = k + (r>0 ? 1 : 3);
                            hffaces1[3*2*f+1] = k + (r>0 ? 3 : 1);

                            // second face in rectangle
                            hffaces1[3*2*f+3] = k;
                            hffaces1[3*2*f+5] = k + (r>0 ? 3 : 2);
                            hffaces1[3*2*f+4] = k + (r>0 ? 2 : 3);
                        }

                    // faces: left and right
                    for( c=0; c<2; c++ )
                        for( r=0; r<nrow-1; r++ )
                        {
                            int f = 2*(ncol-1) + ((c>0?(nrow-1):0)+r);
                            int k = nrow*ncol + 4*ncol + 2*((c>0?nrow:0)+r);

                            // first face in rectangle
                            hffaces1[3*2*f]   = k;
                            hffaces1[3*2*f+2] = k + (c>0 ? 3 : 1);
                            hffaces1[3*2*f+1] = k + (c>0 ? 1 : 3);

                            // second face in rectangle
                            hffaces1[3*2*f+3] = k;
                            hffaces1[3*2*f+5] = k + (c>0 ? 2 : 3);
                            hffaces1[3*2*f+4] = k + (c>0 ? 3 : 2);
                        }

                    Debug.Log(ncol);
                    Debug.Log(nrow);
                    Debug.Log(Mathf.Min(hffaces1));
                    Debug.Log(Mathf.Max(hffaces1));

                    // create mesh with automatic normals and tangents
                    filt.sharedMesh = new Mesh();
                    filt.sharedMesh.vertices = hfvertices;
                    filt.sharedMesh.uv = hfuv;
                    filt.sharedMesh.subMeshCount = 2;
                    filt.sharedMesh.SetTriangles(hffaces0, 0);
                    filt.sharedMesh.SetTriangles(hffaces1, 1);
                    filt.sharedMesh.RecalculateNormals();
                    filt.sharedMesh.RecalculateTangents();

                    // set name
                    StringBuilder hname = new StringBuilder(100);
                    MJP.GetElementName(MJP.TElement.HFIELD, obj.dataid, hname, 100);
                    filt.sharedMesh.name = hname.ToString();
                    break;

                case MJP.TGeom.CAPSULE:
                case MJP.TGeom.MESH:
                    // reuse shared mesh from earlier object
                    if( obj.mesh_shared>=0 )
                        filt.sharedMesh = objects[obj.mesh_shared].GetComponent<MeshFilter>().sharedMesh;

                    // create new mesh
                    else
                    {
                        // copy vertices, normals, uv
                        Vector3[] vertices = new Vector3[obj.mesh_nvertex];
                        Vector3[] normals = new Vector3[obj.mesh_nvertex];
                        Vector2[] uv = new Vector2[obj.mesh_nvertex];
                        for( int k=0; k<obj.mesh_nvertex; k++ )
                        {
                            vertices[k].Set(-obj.mesh_position[3*k],
                                             obj.mesh_position[3*k+2],
                                            -obj.mesh_position[3*k+1]);

                            normals[k].Set(-obj.mesh_normal[3*k],
                                            obj.mesh_normal[3*k+2],
                                           -obj.mesh_normal[3*k+1]);

                            uv[k].Set(obj.mesh_texcoord[2*k],
                                      obj.mesh_texcoord[2*k+1]);
                        }

                        // copy faces
                        int[] faces = new int[3*obj.mesh_nface];
                        for( int k=0; k<obj.mesh_nface; k++ )
                        {
                            faces[3*k]   = obj.mesh_face[3*k];
                            faces[3*k+1] = obj.mesh_face[3*k+2];
                            faces[3*k+2] = obj.mesh_face[3*k+1];
                        }

                        // number of verices can be modified by uncompressed mesh
                        int nvert = obj.mesh_nvertex;

                        // replace with uncompressed mesh when UV needs to be recomputed
                        if( recomputeUV && (MJP.TGeom)obj.geomtype==MJP.TGeom.MESH )
                        {
                            // make temporary mesh
                            Mesh temp = new Mesh();
                            temp.vertices = vertices;
                            temp.normals = normals;
                            temp.triangles = faces;

                            // generate uncompressed UV unwrapping
                            Vector2[] UV = Unwrapping.GeneratePerTriangleUV(temp);
                            int N = UV.GetLength(0)/3;
                            if( N!=obj.mesh_nface )
                                throw new System.Exception("Unexpected number of faces");
                            nvert = 3*N;
                            
                            // create corresponding uncompressed vertices, normals, faces
                            Vector3[] Vertex = new Vector3[3*N];
                            Vector3[] Normal = new Vector3[3*N];
                            int[] Face = new int[3*N];                            
                            for( int k=0; k<N; k++ )
                            {
                                Vertex[3*k]   = vertices[faces[3*k]];
                                Vertex[3*k+1] = vertices[faces[3*k+1]];
                                Vertex[3*k+2] = vertices[faces[3*k+2]];

                                Normal[3*k]   = normals[faces[3*k]];
                                Normal[3*k+1] = normals[faces[3*k+1]];
                                Normal[3*k+2] = normals[faces[3*k+2]];

                                Face[3*k]   = 3*k;
                                Face[3*k+1] = 3*k+1;
                                Face[3*k+2] = 3*k+2;
                            }

                            // create uncompressed mesh
                            filt.sharedMesh = new Mesh();
                            filt.sharedMesh.vertices = Vertex;
                            filt.sharedMesh.normals = Normal;
                            filt.sharedMesh.triangles = Face;
                            filt.sharedMesh.uv = UV;
                        }

                        // otherwise create mesh directly
                        else
                        {
                            filt.sharedMesh = new Mesh();
                            filt.sharedMesh.vertices = vertices;
                            filt.sharedMesh.normals = normals;
                            filt.sharedMesh.triangles = faces;
                            filt.sharedMesh.uv = uv;
                        }

                        // optionally recompute normals for meshes
                        if( recomputeNormal && (MJP.TGeom)obj.geomtype==MJP.TGeom.MESH )
                            filt.sharedMesh.RecalculateNormals();

                        // always calculate tangents (MuJoCo does not support tangents)
                        filt.sharedMesh.RecalculateTangents();

                        // set name
                        if( (MJP.TGeom)obj.geomtype==MJP.TGeom.CAPSULE )
                            filt.sharedMesh.name = "Capsule mesh";
                        else
                        {
                            StringBuilder mname = new StringBuilder(100);
                            MJP.GetElementName(MJP.TElement.MESH, obj.dataid, mname, 100);
                            filt.sharedMesh.name = mname.ToString();
                        }

                        // print error if number of vertices or faces is over 65535
                        if( obj.mesh_nface>65535 || nvert>65535 )
                            Debug.LogError("MESH TOO BIG: " + filt.sharedMesh.name + 
                                           ", vertices " + nvert + ", faces " + obj.mesh_nface);
                    }
                    break;
            }

            // existing material
            if( obj.material>=0 )
            {
                // not modified
                if( obj.color[0]==0.5f && obj.color[1]==0.5f && obj.color[2]==0.5f && obj.color[3]==1 )
                    rend.sharedMaterial = materials[obj.material];

                // color override
                else
                {
                    rend.sharedMaterial = new Material(materials[obj.material]);
                    AdjustMaterial(rend.sharedMaterial, obj.color[0], obj.color[1], obj.color[2], obj.color[3]);
                }
            }

            // new material
            else
            {
                rend.sharedMaterial = new Material(Shader.Find("Standard"));
                AdjustMaterial(rend.sharedMaterial, obj.color[0], obj.color[1], obj.color[2], obj.color[3]);
            }

            // get MuJoCo object transform and set in Unity
            MJP.TTransform transform;
            int visible;
            int selected;
            MJP.GetObjectState(i, &transform, &visible, &selected);
            SetTransform(objects[i], transform);
        }

        // delete primitives
        for( int i=0; i<4; i++ )
            DestroyImmediate(primitives[i]);

        AssetDatabase.Refresh();
    }


    // run importer
    private unsafe void RunImport()
    {
        // adjust global settings
        Time.fixedDeltaTime = 0.005f;
        PlayerSettings.runInBackground = true;
        if( enableRemote )
        {
            QualitySettings.vSyncCount = (noVSync ? 0 : 1);
        }
        else
            QualitySettings.vSyncCount = 1;

        // disable active cameras
        Camera[] activecam = FindObjectsOfType<Camera>();
        foreach( Camera ac in activecam )
            ac.gameObject.SetActive(false);

        // get filename only (not path or extension)
        int i1 = modelFile.LastIndexOf('/');
        int i2 = modelFile.LastIndexOf('.');
        if( i1>=0 && i2>i1 )
            fileName = modelFile.Substring(i1+1, i2-i1-1);
        else
            throw new System.Exception("Unexpected model file format");

        // initialize plugin and load model
        MJP.Initialize();
        MJP.LoadModel(modelFile);

        // get model sizes
        MJP.TSize size;
        MJP.GetSize(&size);

        // save binary model
        MakeDirectory("Assets", "StreamingAssets");
        MJP.SaveMJB("Assets/StreamingAssets/" + fileName + ".mjb");

        // import textures
        if( size.ntexture>0 && importTexture )
        { 
            MakeDirectory("Assets", "Textures");
            ImportTextures(size.ntexture);
        }

        // import materials
        if( size.nmaterial>0 )
        { 
            MakeDirectory("Assets", "Materials");
            ImportMaterials(size.nmaterial);
        }

        // create root, destroy old if present
        root = GameObject.Find("MuJoCo");
        if( root!=null )
            DestroyImmediate(root);
        root = new GameObject("MuJoCo");
        if( root==null )
            throw new System.Exception("Could not create root MuJoCo object");
        
        // add camera to root
        AddCamera();

        // import renderable objects under root
        ImportObjects(size.nobject);

        // attach script to root
        if( enableRemote )
        {
            // add remote
            MJRemote rem = root.GetComponent<MJRemote>();
            if( rem==null )
                rem = root.AddComponent<MJRemote>();
            rem.modelFile = fileName + ".mjb";
            rem.tcpAddress = tcpAddress;
            rem.tcpPort = tcpPort;

            // destroy simulate if present
            if( root.GetComponent<MJSimulate>() )
                DestroyImmediate(root.GetComponent<MJSimulate>());
        }
        else
        {
            // add simulate
            MJSimulate sim = root.GetComponent<MJSimulate>();
            if( sim==null )
                sim = root.AddComponent<MJSimulate>();
            sim.modelFile = fileName + ".mjb";

            // destroy remote if present
            if( root.GetComponent<MJRemote>() )
                DestroyImmediate(root.GetComponent<MJRemote>());
        }

        // close plugin
        MJP.Close();
    }
}

#endif