﻿using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.UI;
using WadTools;
using System.Text;

/*
Used to build a mesh from a level, and apply the correct textures and offsets etc.
*/

public class DoomMapBuilder {

	public delegate void DoneBuilding();
	public DoneBuilding doneBuilding;

	// Object references
	public MapData map;
	public  WadFile wad;
	private GameObject levelObject;
	private GameObject geoObject;
	private GameObject thingsObject;
	private TextureTable textureTable; 
	private SectorTriangulation st;

	// Constanst
	public static float SCALE = 1f/64f;
	public static float PLAYER_HEIGHT = 56f;

	// Thing placement
	public Dictionary<int, Sector> thingSectors;
	private List<int> unclaimedThings;

	// Texture setup
	private Texture2D paletteLookup;
	private Texture2D colormapLookup;
	private Material doomMaterial;
	private Material pngMaterial;
	public static Material skyMaterial;
	private Material spriteMaterial;
	private string skyName = "SKY1";

	// Level building status
	public bool linesDone = false;
	public bool sectorsDone = false;
	int lastBuiltLine = -1;
	int lastBuiltSector = -1;
	bool lastTexturePNG = false;

	bool benchmark;

	public float amountLoaded {
		get {
			if (map == null) return 0.0f;
			return (((float) lastBuiltLine / (float) map.linedefs.Length) + ((float) lastBuiltSector / (float) map.sectors.Length)) * 0.5f;
		}
	}

	public int GetIndexOfThing(int thingType) {
		for (int i = map.things.Length - 1; i >= 0; i--) {
			if (map.things[i].type == thingType) {
				return i;
			}
		}
		return -1;
	}

	public void SetMapInfo(MapInfo mapInfo)
	{
		if(mapInfo != null) skyName = mapInfo.sky;
		else skyName = "SKY1";
	}

	public void BuildMap(WadFile wad, string mapname, bool benchmark = false) {
		
		this.benchmark = benchmark;
		this.wad = wad;
		textureTable = wad.textureTable;

		// Graphics setup. TODO: Move this to DoomGraphic, or somewhere else graphic related.
		paletteLookup = new Palette(wad.GetLump("PLAYPAL")).GetLookupTexture();
		colormapLookup = new Colormap(wad.GetLump("COLORMAP")).GetLookupTexture();
		
		doomMaterial = new Material(Shader.Find("Doom/Texture"));
		doomMaterial.SetTexture("_Palette", paletteLookup);
		doomMaterial.SetTexture("_Colormap", colormapLookup);
		
		pngMaterial = new Material(Shader.Find("Doom/Unlit Truecolor Texture"));
		
		skyMaterial = new Material(Shader.Find("Doom/Sky"));
		skyMaterial.SetTexture("_Palette", paletteLookup);
		skyMaterial.SetTexture("_RenderMap", GetTexture(skyName));
		
		spriteMaterial = new Material(Shader.Find("Doom/Texture"));
		spriteMaterial.SetTexture("_Palette", paletteLookup);
		spriteMaterial.SetTexture("_Colormap", colormapLookup);

		map = MapData.Load(wad, mapname);
		if (map == null) {
			throw new Exception("Loading map failed.");
		}

		st = new SectorTriangulation(map, benchmark);
		levelObject = new GameObject(mapname);
		geoObject = new GameObject("Geometry");
		thingsObject = new GameObject("Things");
		geoObject.transform.SetParent(levelObject.transform);
		thingsObject.transform.SetParent(levelObject.transform);

		// Build thing information
		unclaimedThings = new List<int>();
		for (int i = 0; i < map.things.Length; i++) {
			unclaimedThings.Add(i);
		}
		thingSectors = new Dictionary<int, Sector>();

		CoroutineRunner cr = levelObject.AddComponent<CoroutineRunner>();
		cr.dmb = this;
		cr.map = map;
		linesDone = false;
		sectorsDone = false;

		cr.StartCoroutine(cr.BuildLines());
		cr.StartCoroutine(cr.BuildSectors());

		levelObject.transform.localScale = new Vector3(SCALE,SCALE * 1.2f,SCALE);
	}

	public void BuildMap(string wadpath, string mapname) {
		wad = new WadFile(wadpath);
		BuildMap(wad, mapname);
	}

	public void BuildMap(string mapname) {
		BuildMap(wad, mapname);
	}

	public void DoneBuildingSectors() {
		sectorsDone = true;
		// List of all things that haven't been placed in a sector
		if (unclaimedThings.Count > 0) {
			for (int i = 0; i < unclaimedThings.Count; i++) {
				Debug.LogWarning("Unclaimed Thing: "+unclaimedThings[i]);
			}
		}
		if (linesDone) {
			Cleanup();
			doneBuilding();
		}
	}

	public void DoneBuildingLines () {
		linesDone = true;
		if (sectorsDone) {
			Cleanup();
			doneBuilding();
		}
	}

	void Cleanup() {
		if (benchmark) {
			st.PrintDebugTimes();
		}

		GameObject worldMesh = new GameObject("WorldMesh");
		MeshRenderer meshRenderer = worldMesh.AddComponent<MeshRenderer>();
		worldMesh.AddComponent<MeshCollider>();

		MeshFilter[] meshFilters = geoObject.GetComponentsInChildren<MeshFilter>();
		CombineInstance[] combine = new CombineInstance[meshFilters.Length];
		Material[] materials = new Material[meshFilters.Length];
		for (int i = 0; i < meshFilters.Length; i++) {
			combine[i].mesh = meshFilters[i].sharedMesh;
			combine[i].transform = meshFilters[i].transform.localToWorldMatrix;
			//meshFilters[i].gameObject.SetActive(false);
			MeshRenderer mr = meshFilters[i].gameObject.GetComponent<MeshRenderer>();
			materials[i] = mr.material;
			GameObject.Destroy(mr);
		}

		meshRenderer.materials = materials;

		MeshFilter meshFilter = worldMesh.AddComponent<MeshFilter>();
		meshFilter.mesh = new Mesh();
		meshFilter.mesh.CombineMeshes(combine, false);

		worldMesh.transform.parent = levelObject.transform;

		if (doneBuilding != null) doneBuilding();
	}

	public void BuildLevelEntities(MultigenParser multigen) {
		for (int i = 0; i < map.things.Length; i++) {
			if (!map.things[i].multiplayer) {
				MultigenObject mobj = multigen.GetObjectByDoomedNum(map.things[i].type);
				if (mobj != null && thingSectors.ContainsKey(i)) {
					GameObject newObj = new GameObject(mobj.name);
					newObj.transform.localPosition = new Vector3(map.things[i].x * SCALE, thingSectors[i].floorHeight * SCALE * 1.2f, map.things[i].y * SCALE);
					newObj.transform.localScale = new Vector3(1.6f,1.76f,1.6f);
					newObj.transform.parent = thingsObject.transform;

					LevelEntity ent = newObj.AddComponent<LevelEntity>();

					ent.LoadMultigen(multigen, mobj, wad);
					ent.direction = (float) map.things[i].angle;
				}
			}
		}
	}

	// Test a map without building it
	public int TestMap(WadFile wad, string mapname) {
		map = new DoomMapData(wad, mapname);
		st = new SectorTriangulation(map);
		int failedSectors = 0;
		for (int i = 0; i < map.sectors.Length; i++) {
			List<SectorPolygon> polygons = null;
			try {
				polygons = st.Triangulate(i);
			} catch  {
				//Debug.Log("Exception found in "+mapname+" sector "+i);
			}
			if (polygons == null) failedSectors += 1;
		}
		return failedSectors;
	}

	DoomTexture GetInfo(string name, bool tryUpper = true) {
		if (textureTable.Contains(name)) {
			return textureTable.Get(name.ToUpper());
		} else {
			if (wad.Contains(name)) {
				DataType type = wad.DetectType(name);
				if (type == DataType.DoomFlat) {
					return new DoomTexture(name, 64, 64, null);
				} else if (type == DataType.PNG) {
					Texture2D image = new Texture2D(2,2, TextureFormat.ARGB32, false);
					ImageConversion.LoadImage(image, wad.GetLump(name));
					image.filterMode = FilterMode.Point;
					return new DoomTexture(name, image.width, image.height, null);
				} else {
					throw new Exception("Unknown texture type: "+name);
				}
			} else {
				if (tryUpper) {
					return GetInfo(name.ToUpper(), false);
				}
				throw new Exception("Cannot find texture: "+name);
			}
		}
	}

	public void BuildSector(int index) {

		if (map.sectors[index].ceilingTexture == "-" && map.sectors[index].floorTexture == "-") return;

	 	//st = new SectorTriangulation(map);
		List<SectorPolygon> polygons = st.Triangulate(index);

		if (polygons == null) return;

		for (int i = 0; i < polygons.Count; i++) {
			for (int t = 0; t < unclaimedThings.Count; t++) {
				if (polygons[i].ThingInside(map.things[unclaimedThings[t]])) {
					thingSectors.Add(unclaimedThings[t], map.sectors[index]);
					unclaimedThings.RemoveAt(t);
					t-=1;
				}
			}
		}

		int floorHeight = map.sectors[index].floorHeight;
		int ceilingHeight = map.sectors[index].ceilingHeight;

		float brightness = map.sectors[index].lightLevel /256f;
		

		for (int i = 0; i < polygons.Count; i++) {
			Mesh mesh;
			Vector3[] vertices;
			int[] tris;
			Vector2[] uvs;

			
			mesh = new Mesh();
			vertices = polygons[i].PointsToVector3(floorHeight).ToArray();
			tris = polygons[i].triangles.ToArray();
			uvs = polygons[i].points.ToArray();

			for (int j = 0; j < uvs.Length; j++) {
				uvs[j] /= 64f;
			}

			mesh.vertices = vertices;
			mesh.triangles = tris;
			mesh.uv = uvs;
			if (map.sectors[index].floorTexture != "-") {
				GameObject newObj = new GameObject();
				MeshRenderer mr = newObj.AddComponent<MeshRenderer>();

				newObj.AddComponent<MeshCollider>().sharedMesh = mesh;
				if (map.sectors[index].floorTexture != "F_SKY1") {
					Texture2D tex = GetFlat(map.sectors[index].floorTexture);
					if (lastTexturePNG) {
						mr.material = pngMaterial;
					} else {
						mr.material = doomMaterial;
					}
					
					mr.material.SetTexture("_MainTex", tex);
					mr.material.SetFloat("_Brightness", brightness);
				} else {
					mr.material = skyMaterial;
				}
				newObj.AddComponent<MeshFilter>().mesh = mesh;
				newObj.transform.SetParent(geoObject.transform, false);
				newObj.name = "Floor "+index.ToString();
			}
			
			mesh = new Mesh();
			Array.Reverse(tris);
			for (int j = 0; j < vertices.Length; j++) {
				vertices[j].y = ceilingHeight;
			}
			mesh.vertices = vertices;
			mesh.triangles = tris;
			mesh.uv = uvs;
			if (map.sectors[index].ceilingTexture != "-") {
				GameObject newObj = new GameObject();
				newObj.AddComponent<MeshCollider>().sharedMesh = mesh;
				MeshRenderer mr = newObj.AddComponent<MeshRenderer>();
				if (map.sectors[index].ceilingTexture != "F_SKY1") {
					Texture2D tex = GetFlat(map.sectors[index].ceilingTexture);
					if (lastTexturePNG) {
						mr.material = pngMaterial;
					} else {
						mr.material = doomMaterial;
					}
					mr.material.SetTexture("_MainTex", tex);
					mr.material.SetFloat("_Brightness", brightness);
				} else {
					mr.material = skyMaterial;
				}
				newObj.AddComponent<MeshFilter>().mesh = mesh;
				newObj.transform.SetParent(geoObject.transform, false);
				newObj.name = "Ceiling "+index.ToString();
			}
		}
		lastBuiltSector = index;
		
	}

	public void BuildLine(int index) {
		Linedef line = map.linedefs[index];
		Sidedef frontSide = map.sidedefs[(int) line.front];

		Sidedef backSide = null;
		if (line.back != 0xFFFF && line.back != -1) {
			backSide = map.sidedefs[line.back];
		}

		Sector frontSector = map.sectors[frontSide.sector];
		Sector backSector = backSide!=null?map.sectors[backSide.sector]:null;

		int x1 = map.vertices[line.start].x;
		int y1 = map.vertices[line.start].y;
		int x2 = map.vertices[line.end].x;
		int y2 = map.vertices[line.end].y;

		Vector2 frontOffset = new Vector2(frontSide.xOffset, frontSide.yOffset);
		Vector2 backOffset = new Vector2(frontSide.xOffset, frontSide.yOffset);

		bool upperSky = false;
		bool lowerSky = false;

		if (backSide != null) {
			backOffset = new Vector2(backSide.xOffset, backSide.yOffset);
			upperSky = (frontSector.ceilingTexture == "F_SKY1" && backSector.ceilingTexture == "F_SKY1");
			lowerSky = (frontSector.floorTexture == "F_SKY1" && backSector.floorTexture == "F_SKY1");
		}

		float frontBrightness = map.sectors[frontSide.sector].lightLevel /256f;
		float backBrightness = 1f;
		if (backSide != null) {
			backBrightness = map.sectors[backSide.sector].lightLevel /256f;
		}
		

		if (backSector == null) {
			int z1 = frontSector.floorHeight;
			int z2 = frontSector.ceilingHeight;

			if (line.lowerUnpegged) frontOffset.y += GetInfo(frontSide.mid).height - (z2-z1);
			// Just Midtex
			BuildQuad(index, new Vector3(x1, z1, y1), new Vector3(x2,z2,y2), frontSide.mid, frontOffset, frontBrightness, true);
		} else {
			// Lower texture
			float midBottom;
			Vector2 offset = new Vector2();
			if (frontSector.floorHeight < backSector.floorHeight) {
				// Front Side
				offset.Set(frontOffset.x, frontOffset.y);
				if (frontSide.lower != "-" || lowerSky) {
					// PEGGED: top of texture matches highest floor height
					// UNPEGGED: top of texture matches hightest CEILING
					if (line.lowerUnpegged && !lowerSky) { 
						// difference between top of floor and top of heighest ceiling
						// ceiling height - floor height
						float diff = Mathf.Max(frontSector.ceilingHeight, backSector.ceilingHeight) - backSector.floorHeight;
						offset.y -= GetInfo(frontSide.lower).height - diff;
					}
					BuildQuad(index, new Vector3(x1,frontSector.floorHeight, y1), new Vector3(x2, backSector.floorHeight, y2), frontSide.lower, offset, frontBrightness, true, lowerSky);
				}
				midBottom = backSector.floorHeight;
			} else {
				// Back Side
				offset.Set(backOffset.x, backOffset.y);
				if (backSide.lower != "-" || lowerSky) {
					// PEGGED: top of texture matches highest floor height
					// UNPEGGED: top of texture matches hightest CEILING
					if (line.lowerUnpegged && !lowerSky) { 
						float diff = Mathf.Max(frontSector.ceilingHeight, backSector.ceilingHeight) - frontSector.floorHeight;
						offset.y -= GetInfo(backSide.lower).height - diff;
					}
					BuildQuad(index, new Vector3(x2, backSector.floorHeight, y2), new Vector3(x1, frontSector.floorHeight, y1), backSide.lower, offset, backBrightness, true, lowerSky);
				}
				midBottom = frontSector.floorHeight;
			}

			// Upper texture
			float midTop;
			if (frontSector.ceilingHeight < backSector.ceilingHeight) {
				// Back Side
				offset.Set(backOffset.x, backOffset.y);
				if (backSide.upper != "-" || upperSky) { 
					// PEGGED: bottom of texture matches lower ceiling height
					// UNPEGGED: top of texture is the upper ceiling height
					if (!line.upperUnpegged && !upperSky) {
						offset.y += GetInfo(backSide.upper).height - (backSector.ceilingHeight-frontSector.ceilingHeight);
					}

					BuildQuad(index, new Vector3(x2, frontSector.ceilingHeight, y2), new Vector3(x1, backSector.ceilingHeight, y1), backSide.upper, offset, backBrightness, true, upperSky);
				}
				midTop = frontSector.ceilingHeight;
			} else {
				// Front Side
				offset.Set(frontOffset.x, frontOffset.y);
				if (frontSide.upper != "-" || upperSky) {
					// PEGGED: bottom of texture matches lower ceiling height
					// UNPEGGED: top of texture is the upper ceiling height
					if (!line.upperUnpegged && !upperSky) {
						offset.y += GetInfo(frontSide.upper).height - (frontSector.ceilingHeight-backSector.ceilingHeight);
					}
					BuildQuad(index, new Vector3(x1, backSector.ceilingHeight, y1), new Vector3(x2, frontSector.ceilingHeight, y2), frontSide.upper, offset, frontBrightness, true, upperSky);
				}
				midTop = backSector.ceilingHeight;
			}

			// Mid textures
			if (frontSide.mid != "-") {
				offset.Set(frontOffset.x, frontOffset.y);
				float fmidTop;
				float fmidBottom;
				if (line.lowerUnpegged) {
					fmidTop = (midBottom + GetInfo(frontSide.mid).height) + frontOffset.y;
					fmidBottom = Mathf.Max(midBottom, midBottom + offset.y);
				} else {
					fmidTop = Mathf.Min(midTop, offset.y + midTop);
					fmidBottom = Mathf.Max(midBottom, midTop + offset.y - GetInfo(frontSide.mid).height);
				}

				BuildQuad(index, new Vector3(x1, fmidBottom, y1), new Vector3(x2,fmidTop,y2), frontSide.mid, offset, frontBrightness, line.impassable);
			}
			if (backSide.mid != "-") {
				offset.Set(backOffset.x, backOffset.y);
				float bmidTop;
				float bmidBottom;
				if (line.lowerUnpegged) {
					bmidTop = (midBottom + GetInfo(backSide.mid).height) + backOffset.y;
					bmidBottom = Mathf.Max(midBottom, midBottom + offset.y);
				} else {
					bmidTop = Mathf.Min(midTop, offset.y + midTop);
					bmidBottom = Mathf.Max(midBottom, midTop + offset.y - GetInfo(backSide.mid).height);
				}
				BuildQuad(index, new Vector3(x2, bmidBottom, y2), new Vector3(x1,bmidTop,y1), backSide.mid, offset, backBrightness, line.impassable);
			}
		}

		lastBuiltLine = index;
	}

	private  Dictionary<string, Material> materialCache;

	void BuildQuad(int index, Vector3 v1, Vector3 v2, string texture, Vector2 uvOffset, float light, bool impassable, bool sky = false) {
	 	// This is where we discard parts of a line that have no height or texture

	 	if (v1.y == v2.y) {
	 		return;
	 	}

	 	// It doesn't matter that it doesn't have a texture if it is flagged as a sky quad.
		if (texture == "-" && !sky) {
			return;
		}

		if (v1.x == v2.x) {
			light += 1f/16f;
		}

		if (v1.z == v2.z) {
			light -= 1f/16f;
		}

		Mesh mesh = new Mesh();
		Vector3[] vertices = new Vector3[4];
		vertices[0] = new Vector3(v1.x, v1.y, v1.z);
		vertices[1] = new Vector3(v2.x, v1.y, v2.z);
		vertices[2] = new Vector3(v1.x, v2.y, v1.z);
		vertices[3] = new Vector3(v2.x, v2.y, v2.z);
		int[] tri = new int[6] {0, 2, 1, 2, 3, 1};

		Texture2D tex;
		if (!sky) {
			tex = GetTexture(texture);
			uvOffset.x /= tex.width;
			uvOffset.y /= tex.height;
		} else {
			tex = GetTexture(skyName);
		}

		float length = Vector3.Distance(v1, new Vector3(v2.x,v1.y,v2.z));
		float height = (v2.y - v1.y);

		Vector2[] uvs = new Vector2[4] {
			new Vector2(0f, height / tex.height) + uvOffset,
			new Vector2(length / tex.width , height / tex.height) + uvOffset,
			new Vector2(0f, 0f) + uvOffset,
			new Vector2(length / tex.width, 0f) + uvOffset
		};

		mesh.vertices = vertices;
		mesh.triangles = tri;
		mesh.uv = uvs;
		GameObject newObj = new GameObject();
		MeshRenderer mr = newObj.AddComponent<MeshRenderer>();

		if (impassable) {
			newObj.AddComponent<MeshCollider>().sharedMesh = mesh;
		}
		
		if (sky) {
			mr.material = skyMaterial;
		} else {
			if (lastTexturePNG) {
				mr.material = pngMaterial;
			} else {
				mr.material = doomMaterial;
			}
			mr.material.SetTexture("_MainTex", tex);
			mr.material.SetFloat("_Brightness", light);
		}
		newObj.AddComponent<MeshFilter>().mesh = mesh;
		newObj.transform.SetParent(geoObject.transform, false);
		newObj.name = "Line "+index.ToString();
	}

	// Build all the lines and floor/ceiling of a sector (or grab already built), and 
	// flip the normals? We want it to be additive, not subtractive.
	GameObject BuildInverseSector(int sector) {
		return null;
	}



	private Dictionary<string, Texture2D> flatCache;

	Texture2D GetFlat(string name) {
		name = name.ToUpper();

		if (flatCache == null) flatCache = new Dictionary<string, Texture2D>();

		if (flatCache.ContainsKey(name)) return flatCache[name];

		DoomFlat flat;
		if (wad.Contains(name)) {
			Texture2D output;
			DataType type = wad.DetectType(name);
			if (type == DataType.DoomFlat) {
				flat = new DoomFlat(wad.GetLump(name));
				output = flat.ToRenderMap();
				lastTexturePNG = false;
			} else if (type == DataType.PNG) {
				output = new Texture2D(2,2, TextureFormat.ARGB32, false);
				ImageConversion.LoadImage(output, wad.GetLump(name));
				output.filterMode = FilterMode.Point;
				lastTexturePNG = true;
			} else {
				throw new Exception("Unknown flat type: "+name);
			}

			flatCache.Add(name, output);
			return output;
			
		} else {
			if (textureTable.Contains(name)) {
				return DoomGraphic.BuildTexture(name, wad, textureTable);
			}
		}
		
		throw new Exception("Cannot find flat: "+name);
		
		
	}


	Texture2D GetTexture(string name, bool tryUpper = true) {

		if (textureTable.Contains(name)) {
			lastTexturePNG = false;
			return DoomGraphic.BuildTexture(name, wad, textureTable);
		} else {
			if (wad.Contains(name)) {
				DataType type = wad.DetectType(name);
				if (type == DataType.DoomFlat) {
					lastTexturePNG = false;
					return new DoomFlat(wad.GetLump(name)).ToRenderMap();
				} else if (type == DataType.PNG) {
					Texture2D image = new Texture2D(2,2, TextureFormat.ARGB32, false);
					ImageConversion.LoadImage(image, wad.GetLump(name));
					image.filterMode = FilterMode.Point;
					lastTexturePNG = true;
					return image;
				} else {
					throw new Exception("Unknown texture type: "+name);
				}
			} else {
				if (tryUpper) {
					return GetTexture(name.ToUpper(), false);
				}
				throw new Exception("Cannot find texture: "+name);
			}
		}

		
	}

}

// Unity coroutines can only be run from a MonoBehaviour, so we have a "dummy" class for that.

public class CoroutineRunner : MonoBehaviour {
	void Start() {

	}

	private bool backgroundLoading = false;
	public MapData map;
	public DoomMapBuilder dmb;

	public IEnumerator BuildLines() {
		backgroundLoading = Settings.Get("m_backgroundload", "false") == "true";
		yield return null;  // one frame so the game can do an update before any loading happens/
							// this is mainly so the HUD message "switching to mapxx" happens
		float time = Time.realtimeSinceStartup;
		for (int i = 0; i < map.linedefs.Length; i++) {

			dmb.BuildLine(i);

			// Limiting when it yields to speed up processing.
			if (i % 10 == 0 && backgroundLoading) {
				if (Time.realtimeSinceStartup - time > 0.03f) {
					time = Time.realtimeSinceStartup;
					yield return null;
				}
			}
		}
		dmb.DoneBuildingLines();
	}

	public IEnumerator BuildSectors() {
		backgroundLoading = Settings.Get("m_backgroundload", "false") == "true";
		yield return null;
		float time = Time.realtimeSinceStartup;
		for (int i = 0; i < map.sectors.Length; i++) {

			dmb.BuildSector(i);

			if (backgroundLoading) {
				if (Time.realtimeSinceStartup - time > 0.03f) {
					time = Time.realtimeSinceStartup;
					yield return null;
				}
			}
		}
		dmb.DoneBuildingSectors();
	}
}
