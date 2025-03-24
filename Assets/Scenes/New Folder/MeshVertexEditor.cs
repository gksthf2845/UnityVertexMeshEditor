using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Linq;
public class MeshVertexEditor : MonoBehaviour
{
    [Header("편집 설정")]
    [SerializeField] private Color wireframeColor = Color.yellow;
    [SerializeField] private Color vertexColor = Color.red;
    [SerializeField] private Color selectedVertexColor = Color.green;
    [SerializeField] private float vertexSize = 0.05f;
    [SerializeField] private KeyCode resetKey = KeyCode.R;
    [SerializeField] private Color marqueeColor = new Color(0, 0.5f, 1f, 0.3f); // 선택 사각형 색상

    [Header("머테리얼 설정")]
    [SerializeField] private Material vertexMaterial; // 버텍스 머테리얼
    [SerializeField] private Material wireframeMaterial; // 와이어프레임 머테리얼
    [SerializeField] private Material marqueeMaterial; // 선택 사각형 머테리얼

    [Header("표시 설정")]
    [SerializeField] private bool showWireframe = true;
    [SerializeField] private bool showMeshMaterial = true;

    private Mesh originalMesh;
    private Mesh clonedMesh;
    private Vector3[] originalVertices;
    private Vector3[] modifiedVertices;
    private MeshFilter meshFilter;
    private MeshCollider meshCollider;
    private Camera mainCamera;

    private List<GameObject> vertexMarkers = new List<GameObject>();
    private List<GameObject> selectedVertices = new List<GameObject>(); // 다중 선택된 버텍스
    private List<int> selectedVertexIndices = new List<int>(); // 다중 선택된 버텍스 인덱스
    private bool isDragging = false;
    private Vector3 lastMousePosition;
    private bool isEditMode = false;

    // 마커 오브젝트를 위한 부모 오브젝트
    private GameObject markersContainer;

    // 사각형 영역 선택을 위한 변수들
    private bool isMarqueeSelecting = false;
    private Vector3 marqueeStart;
    private GameObject marqueeVisual;

    // 클래스에 새로운 필드 추가
    private Dictionary<int, List<int>> vertexConnections = new Dictionary<int, List<int>>();

    // 클래스 상단에 새로운 필드 추가
    private Dictionary<int, HashSet<int>> mergedVertices = new Dictionary<int, HashSet<int>>();
    [SerializeField] private float mergeDistance = 0.0001f; // 0.001f에서 0.01f로 증가

    // 클래스 상단에 static 변수 추가
    private static MeshVertexEditor currentActiveEditor = null;
    public MeshRenderer meshRenderer;
    public Toggle toggle;
    public void SetWireFrame()
    {
        showMeshMaterial = !toggle.isOn;
    }

    private SkinnedMeshRenderer skinnedMeshRenderer;
    private bool isSkinnedMesh = false;

    void Start()
    {
        mainCamera = Camera.main;
        meshFilter = GetComponent<MeshFilter>();
        meshCollider = GetComponent<MeshCollider>();
        meshRenderer = GetComponent<MeshRenderer>();
        skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();

        // Skinned Mesh 또는 일반 Mesh 확인
        if (skinnedMeshRenderer != null)
        {
            isSkinnedMesh = true;
            originalMesh = skinnedMeshRenderer.sharedMesh;
        }
        else if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            isSkinnedMesh = false;
            originalMesh = meshFilter.sharedMesh;
        }
        else
        {
            Debug.LogError("MeshFilter/SkinnedMeshRenderer 또는 Mesh가 없습니다!");
            enabled = false;
            return;
        }

        // Mesh 복제 및 초기화
        clonedMesh = Instantiate(originalMesh);
        if (!isSkinnedMesh)
        {
            meshFilter.mesh = clonedMesh;
        }
        else
        {
            skinnedMeshRenderer.sharedMesh = clonedMesh;
        }

        originalVertices = originalMesh.vertices;
        modifiedVertices = new Vector3[originalVertices.Length];
        System.Array.Copy(originalVertices, modifiedVertices, originalVertices.Length);

        // MeshCollider가 없다면 생성하지 않음
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = clonedMesh;
        }

        // 중복 호출 제거하고 순서 변경
        BuildVertexConnections();
        BuildMergedVertices();
        ApplyVertexMerging();

        InitMaterials();
        markersContainer = new GameObject("VertexMarkers");
        markersContainer.transform.SetParent(transform);

        if (meshRenderer != null)
        {
            meshRenderer.enabled = showMeshMaterial;
        }

        if (meshCollider != null)
        {
            meshCollider.convex = false;
            meshCollider.sharedMesh = Instantiate(clonedMesh);
        }
    }

    // 머테리얼 초기화
    void InitMaterials()
    {
        // 버텍스 머테리얼이 없으면 기본 머테리얼 생성
        if (vertexMaterial == null)
        {
            vertexMaterial = new Material(Shader.Find("Standard"));
        }

        // 와이어프레임 머테리얼이 없으면 기본 머테리얼 생성
        if (wireframeMaterial == null)
        {
            wireframeMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        // 선택 사각형 머테리얼이 없으면 기본 머테리얼 생성
        if (marqueeMaterial == null)
        {
            marqueeMaterial = new Material(Shader.Find("Transparent/Diffuse"));
            marqueeMaterial.color = marqueeColor;
        }

    }

    void Update()
    {
        // 리셋 키 처리
        if (Input.GetKeyDown(resetKey))
        {
            ResetMesh();
            return;
        }

        // ESC 키로 편집 모드 종료
        if (Input.GetKeyDown(KeyCode.Escape) && isEditMode)
        {
            isEditMode = false;
            DestroyVertexMarkers();
            return;
        }

        // 메쉬가 선택되지 않았다면 선택 처리
        if (!isEditMode)
        {
            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit) && hit.transform == transform)
                {
                    // 다른 에디터가 활성화되어 있다면 비활성화
                    if (currentActiveEditor != null && currentActiveEditor != this)
                    {
                        currentActiveEditor.ExitEditMode();
                    }

                    // 현재 에디터를 활성 에디터로 설정
                    currentActiveEditor = this;
                    isEditMode = true;
                    CreateVertexMarkers();
                    return;
                }
            }
            this.meshRenderer.enabled = true;
            return; // 편집 모드가 아니면 여기서 리턴
        }

        // 버텍스 선택 및 드래그 처리
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            bool hitVertex = false;

            // 모든 버텍스 마커에 대해 검사
            float nearestDistance = float.MaxValue;
            GameObject nearestMarker = null;
            int nearestVertexIndex = -1;

            for (int i = 0; i < vertexMarkers.Count; i++)
            {
                GameObject marker = vertexMarkers[i];
                if (marker == null || marker.GetComponent<LineRenderer>() != null)
                    continue;

                Vector3 screenPoint = mainCamera.WorldToScreenPoint(marker.transform.position);

                // 화면상의 마우스 위치와 버텍스의 거리 계산
                Vector2 mousePos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
                Vector2 markerScreenPos = new Vector2(screenPoint.x, screenPoint.y);
                float distance = Vector2.Distance(mousePos, markerScreenPos);

                // 선택 범위 내에 있고, 가장 가까운 버텍스 찾기
                if (distance < 20f) // 선택 범위 조절 가능
                {
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestMarker = marker;
                        nearestVertexIndex = int.Parse(marker.name.Split('_')[1]);
                    }
                }
            }

            // 가장 가까운 버텍스를 찾았다면 선택 처리
            if (nearestMarker != null)
            {
                hitVertex = true;

                // Shift 키를 누르고 있지 않고, 선택되지 않은 버텍스를 클릭한 경우에만 초기화
                if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift)
                    && !selectedVertexIndices.Contains(nearestVertexIndex))
                {
                    ClearSelection();
                }

                // 현재 버텍스 선택
                if (!selectedVertexIndices.Contains(nearestVertexIndex))
                {
                    selectedVertices.Add(nearestMarker);
                    selectedVertexIndices.Add(nearestVertexIndex);
                    nearestMarker.GetComponent<MeshRenderer>().material.color = selectedVertexColor;

                    // 자동으로 연결된 버텍스들 선택
                    SelectConnectedVertices(nearestVertexIndex);
                }
                else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    // Shift 키를 누른 상태에서 다시 클릭하면 선택 해제
                    int existingIndex = selectedVertexIndices.IndexOf(nearestVertexIndex);
                    selectedVertices.RemoveAt(existingIndex);
                    selectedVertexIndices.RemoveAt(existingIndex);
                    nearestMarker.GetComponent<MeshRenderer>().material.color = vertexColor;
                }

                // 드래그 시작
                if (selectedVertices.Count > 0)
                {
                    isDragging = true;
                    lastMousePosition = Input.mousePosition;
                }
            }

            // 버텍스를 클릭하지 않았고, 현재 드래그 중이 아닐 때만 사각형 선택 시작
            if (!hitVertex && !isDragging)
            {
                StartMarqueeSelection();
            }
        }

        // 사각형 선택 업데이트
        if (isMarqueeSelecting && !isDragging)  // isDragging이 false일 때만 업데이트
        {
            UpdateMarqueeSelection();
        }

        // 마우스 버튼 놓았을 때
        if (Input.GetMouseButtonUp(0))
        {
            if (isMarqueeSelecting && !isDragging)  // isDragging이 false일 때만 처리
            {
                FinishMarqueeSelection();
            }

            isDragging = false;
        }

        // 드래그 처리 (선택된 버텍스들이 있고 마우스 버튼이 눌려있는 경우)
        if (isDragging && selectedVertices.Count > 0 && Input.GetMouseButton(0))
        {
            // 현재 마우스 위치를 스크린 공간에서 가져옴
            Vector3 currentMousePosition = Input.mousePosition;

            // 마우스 이동 계산을 위한 현재 선택된 버텍스들의 중심점 계산
            Vector3 centerPosition = Vector3.zero;
            foreach (var vertex in selectedVertices)
            {
                centerPosition += vertex.transform.position;
            }
            centerPosition /= selectedVertices.Count;

            // 카메라 시점에 맞춰 평면 생성 - 카메라 시점 방향을 평면의 노말로 사용
            Plane dragPlane = new Plane(mainCamera.transform.forward, centerPosition);

            // 이전 마우스 위치와 현재 마우스 위치를 월드 공간으로 변환
            Ray prevRay = mainCamera.ScreenPointToRay(lastMousePosition);
            Ray currentRay = mainCamera.ScreenPointToRay(currentMousePosition);

            float prevDistance, currentDistance;
            Vector3 prevWorldPos = Vector3.zero;
            Vector3 currentWorldPos = Vector3.zero;

            if (dragPlane.Raycast(prevRay, out prevDistance) &&
                dragPlane.Raycast(currentRay, out currentDistance))
            {
                prevWorldPos = prevRay.GetPoint(prevDistance);
                currentWorldPos = currentRay.GetPoint(currentDistance);

                // 월드 공간에서의 이동 벡터 계산 - Z축 제한 제거
                Vector3 movement = currentWorldPos - prevWorldPos;

                // 선택된 모든 버텍스 이동
                foreach (int vertexIndex in selectedVertexIndices)
                {
                    // 월드 공간에서 로컬 공간으로의 변환 
                    Vector3 worldPos = transform.TransformPoint(modifiedVertices[vertexIndex]);
                    worldPos += movement;
                    modifiedVertices[vertexIndex] = transform.InverseTransformPoint(worldPos);
                }

                // 선택된 마커 이동
                foreach (GameObject vertex in selectedVertices)
                {
                    vertex.transform.position += movement;
                }

                // 메쉬 업데이트
                UpdateMesh();

                // 와이어프레임 업데이트
                UpdateWireframe();

                // 마우스 위치 업데이트
                lastMousePosition = currentMousePosition;
            }
        }

        // 메쉬 렌더러와 콜라이더 상태 업데이트
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            meshRenderer.enabled = showMeshMaterial;

            // 메쉬 콜라이더도 함께 업데이트
            if (meshCollider != null)
            {
                meshCollider.enabled = showMeshMaterial && !isEditMode;
            }
        }

        // Skinned Mesh의 경우 매 프레임 마커 위치 업데이트
        if (isSkinnedMesh && isEditMode)
        {
            UpdateVertexMarkersPosition();
        }
    }

    // 사각형 선택 시작
    void StartMarqueeSelection()
    {
        isMarqueeSelecting = true;
        isDragging = false;
        marqueeStart = Input.mousePosition;

        // 사각형 선택 시각화 오브젝트 생성 (만약 없으면)
        if (marqueeVisual == null)
        {
            CreateMarqueeVisual();
        }

        marqueeVisual.SetActive(true);
    }

    // 사각형 선택 시각화 오브젝트 생성
    void CreateMarqueeVisual()
    {
        marqueeVisual = new GameObject("MarqueeSelection");
        marqueeVisual.transform.SetParent(markersContainer.transform);

        // 사각형 메쉬 생성
        MeshFilter meshFilter = marqueeVisual.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = marqueeVisual.AddComponent<MeshRenderer>();

        Mesh mesh = new Mesh();
        meshFilter.mesh = mesh;

        // 버텍스 및 삼각형 설정
        mesh.vertices = new Vector3[4] {
            new Vector3(0, 0, 0),
            new Vector3(1, 0, 0),
            new Vector3(0, 1, 0),
            new Vector3(1, 1, 0)
        };

        mesh.triangles = new int[6] { 0, 1, 2, 2, 1, 3 };
        mesh.uv = new Vector2[4] {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };

        // 머테리얼 설정
        if (marqueeMaterial != null)
        {
            meshRenderer.material = marqueeMaterial;
        }
        else
        {
            // 기본 머테리얼 생성
            Material material = new Material(Shader.Find("Transparent/Diffuse"));
            material.color = marqueeColor;
            meshRenderer.material = material;
        }

        // 처음엔 숨김
        marqueeVisual.SetActive(false);
    }

    // 사각형 선택 업데이트
    void UpdateMarqueeSelection()
    {
        Vector3 currentMousePos = Input.mousePosition;

        // 사각형의 왼쪽 아래 (min) 및 오른쪽 위 (max) 코너 계산
        Vector3 min = Vector3.Min(marqueeStart, currentMousePos);
        Vector3 max = Vector3.Max(marqueeStart, currentMousePos);

        // 평면에 레이캐스트하여 월드 공간에서 사각형 위치 구하기
        Ray minRay = mainCamera.ScreenPointToRay(min);
        Ray maxRay = mainCamera.ScreenPointToRay(max);

        Plane marqueeDrawPlane = new Plane(mainCamera.transform.forward, transform.position);

        float minDistance, maxDistance;
        if (marqueeDrawPlane.Raycast(minRay, out minDistance) &&
            marqueeDrawPlane.Raycast(maxRay, out maxDistance))
        {
            Vector3 minWorld = minRay.GetPoint(minDistance);
            Vector3 maxWorld = maxRay.GetPoint(maxDistance);

            // 메쉬 업데이트
            UpdateMarqueeVisual(minWorld, maxWorld);
        }
    }

    // 사각형 선택 종료
    void FinishMarqueeSelection()
    {
        isMarqueeSelecting = false;

        if (marqueeVisual != null)
        {
            marqueeVisual.SetActive(false);
        }

        // 선택 영역 내 버텍스 선택
        SelectVerticesInMarquee();
    }

    // 사각형 시각화 메쉬 업데이트
    void UpdateMarqueeVisual(Vector3 min, Vector3 max)
    {
        if (marqueeVisual == null)
            return;

        MeshFilter meshFilter = marqueeVisual.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.mesh == null)
            return;

        Mesh mesh = meshFilter.mesh;

        // 카메라를 향하도록 회전 행렬 계산
        Vector3 forward = mainCamera.transform.forward;
        Vector3 up = mainCamera.transform.up;
        Vector3 right = mainCamera.transform.right;

        // 메쉬 업데이트 - 카메라 방향에 맞춰 버텍스 위치 조정
        mesh.vertices = new Vector3[4] {
        min,
        min + (max.x - min.x) * right,
        min + (max.y - min.y) * up,
        min + (max.x - min.x) * right + (max.y - min.y) * up
    };

        mesh.RecalculateBounds();
    }

    // 사각형 영역 내 버텍스 선택
    private void SelectVerticesInMarquee()
    {
        Vector3 min = Vector3.Min(marqueeStart, Input.mousePosition);
        Vector3 max = Vector3.Max(marqueeStart, Input.mousePosition);

        min.y = Screen.height - min.y;
        max.y = Screen.height - max.y;

        float temp = min.y;
        min.y = Mathf.Min(temp, max.y);
        max.y = Mathf.Max(temp, max.y);

        Rect marqueeRect = new Rect(min.x, min.y, max.x - min.x, max.y - min.y);

        if (marqueeRect.width < 5 || marqueeRect.height < 5)
            return;

        bool isShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (!isShiftHeld)
        {
            ClearSelection();
        }

        foreach (GameObject marker in vertexMarkers)
        {
            if (marker == null || marker.GetComponent<LineRenderer>() != null)
                continue;

            Vector3 screenPos = mainCamera.WorldToScreenPoint(marker.transform.position);

            // showMeshMaterial이 true일 때만 뒤쪽 버텍스 필터링
            if (showMeshMaterial && screenPos.z < 0)
                continue;

            // y 좌표 변환
            screenPos.y = Screen.height - screenPos.y;

            if (marqueeRect.Contains(new Vector2(screenPos.x, screenPos.y)))
            {
                int vertexIndex = int.Parse(marker.name.Split('_')[1]);

                if (!selectedVertexIndices.Contains(vertexIndex))
                {
                    selectedVertices.Add(marker);
                    selectedVertexIndices.Add(vertexIndex);
                    marker.GetComponent<MeshRenderer>().material.color = selectedVertexColor;
                }
            }
        }
    }

    void ClearSelection()
    {
        // 선택된 모든 버텍스 색상 원래대로 돌리기
        foreach (GameObject vertex in selectedVertices)
        {
            if (vertex != null)
            {
                MeshRenderer renderer = vertex.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.material.color = vertexColor;
            }
        }

        // 선택 정보 초기화
        selectedVertices.Clear();
        selectedVertexIndices.Clear();
    }

    // CreateVertexMarkers() 메서드 수정
    void CreateVertexMarkers()
    {
        DestroyVertexMarkers();

        if (markersContainer == null)
        {
            markersContainer = new GameObject("VertexMarkers");
            markersContainer.transform.SetParent(transform);
        }

        // Skinned Mesh의 경우 현재 포즈의 버텍스 위치 가져오기
        Vector3[] currentVertices;
        if (isSkinnedMesh)
        {
            Mesh bakedMesh = new Mesh();
            skinnedMeshRenderer.BakeMesh(bakedMesh);
            currentVertices = bakedMesh.vertices;
        }
        else
        {
            currentVertices = modifiedVertices;
        }

        // 각 버텍스에 마커 생성
        for (int i = 0; i < currentVertices.Length; i++)
        {
            Vector3 worldPos = transform.TransformPoint(currentVertices[i]);
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Vertex_" + i;
            marker.transform.position = worldPos;
            marker.transform.localScale = Vector3.one * vertexSize;
            marker.transform.SetParent(markersContainer.transform);

            // 콜라이더 활성화 유지
            SphereCollider collider = marker.GetComponent<SphereCollider>();
            collider.isTrigger = false;

            // 재질 설정
            MeshRenderer renderer = marker.GetComponent<MeshRenderer>();

            // 사용자 정의 머테리얼 사용
            if (vertexMaterial != null)
            {
                Material materialInstance = new Material(vertexMaterial);
                materialInstance.color = vertexColor;
                renderer.material = materialInstance;
            }
            else
            {
                // 기본 머테리얼 생성
                renderer.material = new Material(Shader.Find("Standard"));
                renderer.material.color = vertexColor;
            }

            vertexMarkers.Add(marker);
        }

        // 와이어프레임 표시 여부에 따라 처리
        if (showWireframe)
        {
            DrawWireframe();
        }

        // 사각형 선택 시각화 오브젝트 생성
        CreateMarqueeVisual();

        // 편집 모드에서 메쉬 콜라이더 상태 체크
        if (meshCollider != null)
        {
            meshCollider.enabled = showMeshMaterial;
        }
    }

    void DrawWireframe()
    {
        // 메쉬의 삼각형 정보 가져오기
        int[] triangles = clonedMesh.triangles;

        // 삼각형마다 라인을 그림
        for (int i = 0; i < triangles.Length; i += 3)
        {
            if (i + 2 < triangles.Length)
            {
                int v1 = triangles[i];
                int v2 = triangles[i + 1];
                int v3 = triangles[i + 2];

                DrawLine(v1, v2);
                DrawLine(v2, v3);
                DrawLine(v3, v1);
            }
        }
    }

    void DrawLine(int vertIndex1, int vertIndex2)
    {
        Vector3 worldPos1 = transform.TransformPoint(modifiedVertices[vertIndex1]);
        Vector3 worldPos2 = transform.TransformPoint(modifiedVertices[vertIndex2]);

        GameObject lineObj = new GameObject("Line_" + vertIndex1 + "_" + vertIndex2);
        lineObj.transform.SetParent(markersContainer.transform);

        LineRenderer line = lineObj.AddComponent<LineRenderer>();
        line.startWidth = 0.01f;
        line.endWidth = 0.01f;
        line.positionCount = 2;
        line.SetPosition(0, worldPos1);
        line.SetPosition(1, worldPos2);

        // 사용자 정의 와이어프레임 머테리얼 사용
        if (wireframeMaterial != null)
        {
            line.material = wireframeMaterial;
        }
        else
        {
            // 기본 머테리얼 생성
            line.material = new Material(Shader.Find("Sprites/Default"));
        }

        line.startColor = wireframeColor;
        line.endColor = wireframeColor;

        vertexMarkers.Add(lineObj);
    }

    void UpdateWireframe()
    {
        // 와이어프레임 업데이트
        for (int i = 0; i < vertexMarkers.Count; i++)
        {
            GameObject marker = vertexMarkers[i];
            if (marker != null && marker.GetComponent<LineRenderer>() != null)
            {
                LineRenderer line = marker.GetComponent<LineRenderer>();
                string[] parts = marker.name.Split('_');
                if (parts.Length >= 3)
                {
                    int v1 = int.Parse(parts[1]);
                    int v2 = int.Parse(parts[2]);

                    Vector3 worldPos1 = transform.TransformPoint(modifiedVertices[v1]);
                    Vector3 worldPos2 = transform.TransformPoint(modifiedVertices[v2]);

                    line.SetPosition(0, worldPos1);
                    line.SetPosition(1, worldPos2);
                }
            }
        }
    }

    void DestroyVertexMarkers()
    {
        // 모든 마커 제거
        if (markersContainer != null)
        {
            Destroy(markersContainer);
        }

        markersContainer = new GameObject("VertexMarkers");
        markersContainer.transform.SetParent(transform);

        vertexMarkers.Clear();
        selectedVertices.Clear();
        selectedVertexIndices.Clear();
        isDragging = false;
        isMarqueeSelecting = false;
        marqueeVisual = null;
    }

    // UpdateMesh() 메서드 수정
    void UpdateMesh()
    {
        try
        {
            // 메쉬 버텍스 업데이트
            clonedMesh.vertices = modifiedVertices;

            // 메쉬 재계산
            clonedMesh.RecalculateNormals();
            clonedMesh.RecalculateBounds();

            if (isSkinnedMesh)
            {
                // Skinned Mesh의 경우 바운드 업데이트
                skinnedMeshRenderer.localBounds = clonedMesh.bounds;
            }

            // 콜라이더 업데이트
            if (meshCollider != null)
            {
                // 임시로 콜라이더 비활성화
                bool wasEnabled = meshCollider.enabled;
                meshCollider.enabled = false;

                // 새로운 메시 인스턴스 생성
                Mesh colliderMesh = Instantiate(clonedMesh);
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = colliderMesh;

                // 원래 상태로 복원
                meshCollider.enabled = wasEnabled;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Mesh update failed: {e.Message}");
        }
    }

    void ResetMesh()
    {
        // 모든 버텍스를 원래 위치로 복원
        System.Array.Copy(originalVertices, modifiedVertices, originalVertices.Length);
        UpdateMesh();

        // 선택 초기화
        ClearSelection();

        // 편집 모드였다면 마커 위치도 업데이트
        if (isEditMode)
        {
            DestroyVertexMarkers();
            CreateVertexMarkers();
        }
    }

    void OnDestroy()
    {
        // 가비지 컬렉션을 위해 모든 마커 제거
        if (markersContainer != null)
        {
            Destroy(markersContainer);
        }

        // 현재 활성 에디터가 이 인스턴스라면 null로 설정
        if (currentActiveEditor == this)
        {
            currentActiveEditor = null;
        }
    }

    // 새로운 메서드 추가
    private void BuildVertexConnections()
    {
        int[] triangles = clonedMesh.triangles;
        vertexConnections.Clear();

        // 모든 버텍스에 대한 연결 정보 초기화
        for (int i = 0; i < modifiedVertices.Length; i++)
        {
            vertexConnections[i] = new List<int>();
        }

        // 삼각형을 순회하며 연결된 버텍스 정보 구축
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int v1 = triangles[i];
            int v2 = triangles[i + 1];
            int v3 = triangles[i + 2];

            // 양방향 연결 추가
            AddConnection(v1, v2);
            AddConnection(v2, v3);
            AddConnection(v3, v1);
        }
    }

    private void AddConnection(int v1, int v2)
    {
        if (!vertexConnections[v1].Contains(v2))
        {
            vertexConnections[v1].Add(v2);
        }
        if (!vertexConnections[v2].Contains(v1))
        {
            vertexConnections[v2].Add(v1);
        }
    }

    private bool IsCornerVertex(int vertexIndex)
    {
        // 해당 버텍스에 연결된 엣지가 2개 이상이면 선택
        return vertexConnections.ContainsKey(vertexIndex) &&
               vertexConnections[vertexIndex].Count >= 2;
    }

    // BuildVertexConnections() 메서드 뒤에 추가
    private void SelectAllConnectedCorners(int startVertexIndex)
    {
        if (!IsCornerVertex(startVertexIndex))
            return;

        HashSet<int> visitedVertices = new HashSet<int>();
        Queue<int> vertexQueue = new Queue<int>();

        vertexQueue.Enqueue(startVertexIndex);
        visitedVertices.Add(startVertexIndex);

        while (vertexQueue.Count > 0)
        {
            int currentVertex = vertexQueue.Dequeue();

            // 현재 버텍스가 코너이면 선택
            if (IsCornerVertex(currentVertex) && !selectedVertexIndices.Contains(currentVertex))
            {
                GameObject marker = vertexMarkers.Find(m => m.name == "Vertex_" + currentVertex);
                if (marker != null)
                {
                    selectedVertices.Add(marker);
                    selectedVertexIndices.Add(currentVertex);
                    marker.GetComponent<MeshRenderer>().material.color = selectedVertexColor;
                }
            }

            // 연결된 버텍스들을 확인
            foreach (int connectedVertex in vertexConnections[currentVertex])
            {
                if (!visitedVertices.Contains(connectedVertex))
                {
                    visitedVertices.Add(connectedVertex);
                    vertexQueue.Enqueue(connectedVertex);
                }
            }
        }
    }

    // BuildVertexConnections() 메서드 뒤에 새로운 메서드 추가
    private void BuildMergedVertices()
    {
        mergedVertices.Clear();
        Dictionary<int, int> vertexToGroupMap = new Dictionary<int, int>();
        int currentGroup = 0;

        Vector3[] worldVertices = new Vector3[modifiedVertices.Length];
        for (int i = 0; i < modifiedVertices.Length; i++)
        {
            worldVertices[i] = transform.TransformPoint(modifiedVertices[i]);
        }

        // 더 정확한 병합을 위해 월드 공간에서의 거리를 사용
        float sqrMergeDistance = mergeDistance * mergeDistance;

        for (int i = 0; i < worldVertices.Length; i++)
        {
            if (vertexToGroupMap.ContainsKey(i))
                continue;

            HashSet<int> currentMergeGroup = new HashSet<int> { i };
            Queue<int> verticesToCheck = new Queue<int>();
            verticesToCheck.Enqueue(i);

            while (verticesToCheck.Count > 0)
            {
                int currentVertex = verticesToCheck.Dequeue();
                Vector3 currentPos = worldVertices[currentVertex];

                // 연결된 버텍스들 먼저 확인
                if (vertexConnections.ContainsKey(currentVertex))
                {
                    foreach (int connectedVertex in vertexConnections[currentVertex])
                    {
                        if (connectedVertex != currentVertex && !vertexToGroupMap.ContainsKey(connectedVertex))
                        {
                            float sqrDist = (worldVertices[connectedVertex] - currentPos).sqrMagnitude;
                            if (sqrDist <= sqrMergeDistance)
                            {
                                currentMergeGroup.Add(connectedVertex);
                                vertexToGroupMap[connectedVertex] = currentGroup;
                                verticesToCheck.Enqueue(connectedVertex);
                            }
                        }
                    }
                }

                // 나머지 모든 버텍스 확인
                for (int j = 0; j < worldVertices.Length; j++)
                {
                    if (j != currentVertex && !vertexToGroupMap.ContainsKey(j))
                    {
                        float sqrDist = (worldVertices[j] - currentPos).sqrMagnitude;
                        if (sqrDist <= sqrMergeDistance)
                        {
                            currentMergeGroup.Add(j);
                            vertexToGroupMap[j] = currentGroup;
                            verticesToCheck.Enqueue(j);
                        }
                    }
                }
            }

            if (currentMergeGroup.Count > 1)
            {
                mergedVertices[i] = currentMergeGroup;
                vertexToGroupMap[i] = currentGroup;
                currentGroup++;
            }
        }
    }

    // SelectConnectedVertices 메서드 수정
    private void SelectConnectedVertices(int vertexIndex)
    {
        HashSet<int> verticesToSelect = new HashSet<int>();
        Queue<int> verticesToCheck = new Queue<int>();

        verticesToCheck.Enqueue(vertexIndex);
        verticesToSelect.Add(vertexIndex);

        // 현재 버텍스의 월드 위치
        Vector3 basePosition = transform.TransformPoint(modifiedVertices[vertexIndex]);

        while (verticesToCheck.Count > 0)
        {
            int currentVertex = verticesToCheck.Dequeue();
            Vector3 currentPos = transform.TransformPoint(modifiedVertices[currentVertex]);

            // 같은 위치에 있는 다른 버텍스들 찾기
            for (int i = 0; i < modifiedVertices.Length; i++)
            {
                if (!verticesToSelect.Contains(i))
                {
                    Vector3 checkPos = transform.TransformPoint(modifiedVertices[i]);
                    if (Vector3.Distance(currentPos, checkPos) < mergeDistance)
                    {
                        verticesToSelect.Add(i);
                        verticesToCheck.Enqueue(i);
                    }
                }
            }
        }

        // 찾은 모든 버텍스 선택
        foreach (int vertex in verticesToSelect)
        {
            GameObject marker = vertexMarkers.Find(m => m.name == "Vertex_" + vertex);
            if (marker != null && !selectedVertexIndices.Contains(vertex))
            {
                selectedVertices.Add(marker);
                selectedVertexIndices.Add(vertex);
                marker.GetComponent<MeshRenderer>().material.color = selectedVertexColor;
            }
        }
    }

    // 새로운 헬퍼 메서드 추가
    private void SelectVertexAndConnected(int vertexIndex)
    {
        if (!vertexConnections.ContainsKey(vertexIndex))
            return;

        // 현재 버텍스 선택
        SelectVertex(vertexIndex);

        // 연결된 버텍스들 선택
        foreach (int connectedVertex in vertexConnections[vertexIndex])
        {
            SelectVertex(connectedVertex);
        }
    }

    private void SelectVertex(int vertexIndex)
    {
        if (selectedVertexIndices.Contains(vertexIndex))
            return;

        GameObject marker = vertexMarkers.Find(m => m.name == "Vertex_" + vertexIndex);
        if (marker != null)
        {
            selectedVertices.Add(marker);
            selectedVertexIndices.Add(vertexIndex);
            marker.GetComponent<MeshRenderer>().material.color = selectedVertexColor;
        }
    }

    // 클래스에 새로운 메서드 추가
    private void ExitEditMode()
    {
        isEditMode = false;
        DestroyVertexMarkers();

        // 편집 모드 종료 시 메쉬 콜라이더 상태 복원
        if (meshCollider != null)
        {
            meshCollider.enabled = showMeshMaterial;
        }

        if (currentActiveEditor == this)
        {
            currentActiveEditor = null;
        }
    }

    // ApplyVertexMerging() 메서드 수정
    private void ApplyVertexMerging()
    {
        try
        {
            bool hasChanges = false;

            // 각 병합 그룹에 대해
            foreach (var group in mergedVertices)
            {
                if (group.Value.Count > 1)
                {
                    // 그룹의 평균 위치 계산
                    Vector3 averagePosition = Vector3.zero;
                    foreach (int vertexIndex in group.Value)
                    {
                        averagePosition += modifiedVertices[vertexIndex];
                    }
                    averagePosition /= group.Value.Count;

                    // 그룹의 모든 버텍스를 평균 위치로 이동
                    foreach (int vertexIndex in group.Value)
                    {
                        if (Vector3.Distance(modifiedVertices[vertexIndex], averagePosition) > 0.0001f)
                        {
                            modifiedVertices[vertexIndex] = averagePosition;
                            hasChanges = true;
                        }
                    }
                }
            }

            // 변경사항이 있을 때만 메쉬 업데이트
            if (hasChanges)
            {
                UpdateMesh();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Vertex merging failed: {e.Message}");
        }
    }

    private void UpdateVertexMarkersPosition()
    {
        if (!isSkinnedMesh) return;

        // Skinned Mesh의 현재 포즈 버텍스 위치 가져오기
        Mesh bakedMesh = new Mesh();
        skinnedMeshRenderer.BakeMesh(bakedMesh);
        Vector3[] currentVertices = bakedMesh.vertices;

        // 마커 위치 업데이트
        for (int i = 0; i < vertexMarkers.Count; i++)
        {
            GameObject marker = vertexMarkers[i];
            if (marker != null && !marker.GetComponent<LineRenderer>())
            {
                int vertexIndex = int.Parse(marker.name.Split('_')[1]);
                if (vertexIndex < currentVertices.Length)
                {
                    marker.transform.position = transform.TransformPoint(currentVertices[vertexIndex]);
                }
            }
        }
    }
}