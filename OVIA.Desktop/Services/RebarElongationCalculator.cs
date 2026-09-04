using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace OVIA.Desktop
{
    /// <summary>
    /// 철근 형상 JSON의 실제 선형을 기준으로 90도 절곡(D2)을 판정하고 절단 필요길이를 계산한다.
    /// AutoCAD 추출 로직은 변경하지 않으며 Desktop 저장 직전에 파생값(final_length)만 계산한다.
    /// </summary>
    public static class RebarElongationCalculator
    {
        public const double D2MinimumAngle = 85D;
        public const double D2MaximumAngle = 95D;

        private static readonly int[] SupportedDiameters = new int[]
        {
            10, 13, 16, 19, 22, 25, 29, 32, 35, 38, 41, 51
        };

        public static RebarElongationResult Calculate(string shapeJsonPath, string rebarSpec, double originalLengthMm)
        {
            RebarElongationResult result = new RebarElongationResult();
            result.OriginalLengthMm = Math.Max(0D, originalLengthMm);
            result.FinalLengthMm = result.OriginalLengthMm;

            int diameterMm;
            if (!TryParseDiameter(rebarSpec, out diameterMm))
            {
                return result;
            }

            result.DiameterMm = diameterMm;

            if (string.IsNullOrWhiteSpace(shapeJsonPath) || !File.Exists(shapeJsonPath))
            {
                return result;
            }

            CadShapeEditDocument document = CadShapeEditDocument.Load(shapeJsonPath);
            int bendCount = CountD2Bends(document);
            result.BendCount = Math.Max(0, bendCount);
            result.DeductionMm = result.DiameterMm * 2D * result.BendCount;
            result.FinalLengthMm = Math.Max(0D, result.OriginalLengthMm - result.DeductionMm);
            return result;
        }

        public static int CountD2Bends(CadShapeEditDocument document)
        {
            if (document == null || document.Elements == null || document.Elements.Count == 0)
            {
                return 0;
            }

            List<ElongationSegment> segments = BuildStructuralSegments(document);
            if (segments.Count < 2)
            {
                return 0;
            }

            double tolerance = ResolveEndpointTolerance(document, segments);
            ElongationGraph graph = ElongationGraph.Build(segments, tolerance);
            List<int> componentEdges = graph.FindPrimaryRebarComponent();

            if (componentEdges.Count < 2)
            {
                return 0;
            }

            List<ElongationPoint> orderedPoints = graph.TraceComponent(componentEdges);
            if (orderedPoints.Count < 3)
            {
                return 0;
            }

            return CountD2Turns(orderedPoints);
        }

        public static bool TryParseDiameter(string rebarSpec, out int diameterMm)
        {
            diameterMm = 0;
            string value = rebarSpec == null ? "" : rebarSpec.Trim().ToUpperInvariant();
            if (value == "")
            {
                return false;
            }

            MatchCollection matches = Regex.Matches(value, @"(?<!\d)(10|13|16|19|22|25|29|32|35|38|41|51)(?!\d)");
            for (int i = 0; i < matches.Count; i++)
            {
                int parsed;
                if (!Int32.TryParse(matches[i].Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    continue;
                }

                for (int j = 0; j < SupportedDiameters.Length; j++)
                {
                    if (SupportedDiameters[j] == parsed)
                    {
                        diameterMm = parsed;
                        return true;
                    }
                }
            }

            return false;
        }

        private static List<ElongationSegment> BuildStructuralSegments(CadShapeEditDocument document)
        {
            List<ElongationSegment> result = new List<ElongationSegment>();

            for (int i = 0; i < document.Elements.Count; i++)
            {
                CadShapeEditElement element = document.Elements[i];
                if (element == null || !string.Equals(element.Type, "LINE", StringComparison.OrdinalIgnoreCase))
                {
                    // TEXT, CIRCLE, ARC는 절곡점으로 직접 세지 않는다.
                    // CAD 곡선은 추출 단계에서 LINE 샘플로 저장되므로 아래 누적 회전 판정에서 처리된다.
                    continue;
                }

                if (IsIgnoredManualGroup(element.ObjectGroupKind))
                {
                    // 사용자가 추가한 원/타원, 나사, 사각형은 철근 절곡 형상이 아니다.
                    continue;
                }

                ElongationPoint a = new ElongationPoint(element.X1, element.Y1);
                ElongationPoint b = new ElongationPoint(element.X2, element.Y2);
                double length = a.DistanceTo(b);
                if (length <= 0.000001D)
                {
                    continue;
                }

                result.Add(new ElongationSegment(a, b, length));
            }

            AddCollinearGapBridges(document, result);
            return result;
        }

        /// <summary>
        /// 사각형/나사 등의 비구조 객체가 철근 선 위에 삽입되면서 원본 LINE이 여러 구간으로
        /// 끊겨 저장되는 경우가 있다. 이때 구조 LINE의 열린 끝점 중 서로 같은 직선상에 있는
        /// 가장 가까운 쌍만 가상 연결하여 철근의 연속 경로를 복원한다.
        /// 가상 연결선 자체는 절곡을 만들지 않으므로 D2 횟수에는 영향을 주지 않는다.
        /// </summary>
        private static void AddCollinearGapBridges(CadShapeEditDocument document, List<ElongationSegment> segments)
        {
            if (document == null || segments == null || segments.Count < 2)
            {
                return;
            }

            double endpointTolerance = ResolveEndpointTolerance(document, segments);
            double basis = Math.Max(Math.Abs(document.Width), Math.Abs(document.Height));
            if (basis <= 0D)
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    basis = Math.Max(basis, segments[i].Length);
                }
            }

            // 비구조 기호가 차지하는 간격만 복원하고, 멀리 떨어진 별도 철근까지 연결하지 않는다.
            double maxGap = Math.Max(endpointTolerance * 4D, basis * 0.45D);
            List<LooseEndpoint> loose = FindLooseEndpoints(segments, endpointTolerance);
            HashSet<int> used = new HashSet<int>();

            while (true)
            {
                int bestA = -1;
                int bestB = -1;
                double bestDistance = Double.MaxValue;

                for (int i = 0; i < loose.Count; i++)
                {
                    if (used.Contains(i)) continue;

                    for (int j = i + 1; j < loose.Count; j++)
                    {
                        if (used.Contains(j)) continue;
                        if (loose[i].SegmentIndex == loose[j].SegmentIndex) continue;

                        double distance = loose[i].Point.DistanceTo(loose[j].Point);
                        if (distance <= endpointTolerance || distance > maxGap || distance >= bestDistance)
                        {
                            continue;
                        }

                        if (!IsStraightContinuation(loose[i], loose[j], 5D))
                        {
                            continue;
                        }

                        bestA = i;
                        bestB = j;
                        bestDistance = distance;
                    }
                }

                if (bestA < 0 || bestB < 0)
                {
                    break;
                }

                segments.Add(new ElongationSegment(loose[bestA].Point, loose[bestB].Point, bestDistance));
                used.Add(bestA);
                used.Add(bestB);
            }
        }

        private static List<LooseEndpoint> FindLooseEndpoints(List<ElongationSegment> segments, double tolerance)
        {
            List<LooseEndpoint> result = new List<LooseEndpoint>();

            for (int i = 0; i < segments.Count; i++)
            {
                AddIfLooseEndpoint(result, segments, i, true, tolerance);
                AddIfLooseEndpoint(result, segments, i, false, tolerance);
            }

            return result;
        }

        private static void AddIfLooseEndpoint(List<LooseEndpoint> result, List<ElongationSegment> segments, int segmentIndex, bool useA, double tolerance)
        {
            ElongationSegment segment = segments[segmentIndex];
            ElongationPoint point = useA ? segment.A : segment.B;
            int connectionCount = 0;

            for (int i = 0; i < segments.Count; i++)
            {
                if (i == segmentIndex) continue;
                if (segments[i].A.DistanceTo(point) <= tolerance || segments[i].B.DistanceTo(point) <= tolerance)
                {
                    connectionCount++;
                }
            }

            if (connectionCount == 0)
            {
                ElongationPoint inside = useA ? segment.B : segment.A;
                result.Add(new LooseEndpoint(segmentIndex, point, inside));
            }
        }

        private static bool IsStraightContinuation(LooseEndpoint a, LooseEndpoint b, double toleranceDegrees)
        {
            double bridgeX = b.Point.X - a.Point.X;
            double bridgeY = b.Point.Y - a.Point.Y;
            double bridgeLength = Math.Sqrt(bridgeX * bridgeX + bridgeY * bridgeY);
            if (bridgeLength <= 0.000001D)
            {
                return false;
            }

            // 열린 끝점에서 기존 선분의 안쪽 방향과 bridge 방향은 서로 반대쪽이어야 한다.
            // 반대/동일 여부보다 '같은 직선축'인지가 중요하므로 0~180도를 접어서 비교한다.
            return AxisAngleDegrees(a.Inside.X - a.Point.X, a.Inside.Y - a.Point.Y, bridgeX, bridgeY) <= toleranceDegrees
                && AxisAngleDegrees(b.Inside.X - b.Point.X, b.Inside.Y - b.Point.Y, -bridgeX, -bridgeY) <= toleranceDegrees;
        }

        private static double AxisAngleDegrees(double ax, double ay, double bx, double by)
        {
            double aLength = Math.Sqrt(ax * ax + ay * ay);
            double bLength = Math.Sqrt(bx * bx + by * by);
            if (aLength <= 0.000001D || bLength <= 0.000001D)
            {
                return 180D;
            }

            double cos = (ax * bx + ay * by) / (aLength * bLength);
            cos = Math.Max(-1D, Math.Min(1D, cos));
            double angle = Math.Acos(cos) * 180D / Math.PI;
            return Math.Min(angle, 180D - angle);
        }

        private static bool IsIgnoredManualGroup(string objectGroupKind)
        {
            string kind = objectGroupKind == null ? "" : objectGroupKind.Trim().ToUpperInvariant();
            return kind == "ELLIPSE" || kind == "SCREW" || kind == "RECTANGLE";
        }

        private static double ResolveEndpointTolerance(CadShapeEditDocument document, List<ElongationSegment> segments)
        {
            double basis = Math.Max(Math.Abs(document.Width), Math.Abs(document.Height));
            if (basis <= 0D)
            {
                for (int i = 0; i < segments.Count; i++)
                {
                    basis = Math.Max(basis, segments[i].Length);
                }
            }

            return Math.Max(0.02D, basis * 0.0025D);
        }

        private static int CountD2Turns(List<ElongationPoint> points)
        {
            int bendCount = 0;
            double accumulatedCurveTurn = 0D;
            int accumulatedSign = 0;
            double maxSegmentLength = 0D;

            for (int i = 1; i < points.Count; i++)
            {
                maxSegmentLength = Math.Max(maxSegmentLength, points[i - 1].DistanceTo(points[i]));
            }

            double shortSegmentThreshold = maxSegmentLength > 0D ? maxSegmentLength * 0.35D : 0D;

            bool isClosed = points.Count >= 4 && points[0].DistanceTo(points[points.Count - 1]) <= 0.000001D;

            for (int i = 1; i < points.Count - 1; i++)
            {
                ElongationPoint previous = points[i - 1];
                ElongationPoint current = points[i];
                ElongationPoint next = points[i + 1];

                double ax = current.X - previous.X;
                double ay = current.Y - previous.Y;
                double bx = next.X - current.X;
                double by = next.Y - current.Y;
                double lengthA = Math.Sqrt(ax * ax + ay * ay);
                double lengthB = Math.Sqrt(bx * bx + by * by);
                if (lengthA <= 0.000001D || lengthB <= 0.000001D)
                {
                    continue;
                }

                double cross = ax * by - ay * bx;
                double dot = ax * bx + ay * by;
                double signedTurn = Math.Atan2(cross, dot) * 180D / Math.PI;
                double absoluteTurn = Math.Abs(signedTurn);

                if (absoluteTurn >= D2MinimumAngle && absoluteTurn <= D2MaximumAngle)
                {
                    bendCount += IsD2AccumulatedTurn(accumulatedCurveTurn) ? 1 : 0;
                    accumulatedCurveTurn = 0D;
                    accumulatedSign = 0;
                    bendCount++;
                    continue;
                }

                // AutoCAD의 ARC/Polyline bulge는 여러 짧은 LINE으로 샘플링된다.
                // 개별 꼭짓점의 작은 회전은 즉시 D2로 확정하지 않고, 한 곡선 구간의 전체 회전량을
                // 끝까지 합산한 뒤 최종값이 85~95도일 때만 1회 절곡으로 인정한다.
                if (absoluteTurn >= 0.5D && absoluteTurn < 30D)
                {
                    int sign = signedTurn >= 0D ? 1 : -1;
                    bool startsNewCurveAfterLongStraight = accumulatedCurveTurn > 0D
                        && shortSegmentThreshold > 0D
                        && lengthA > shortSegmentThreshold
                        && lengthB <= shortSegmentThreshold;

                    if ((accumulatedSign != 0 && sign != accumulatedSign) || startsNewCurveAfterLongStraight)
                    {
                        bendCount += IsD2AccumulatedTurn(accumulatedCurveTurn) ? 1 : 0;
                        accumulatedCurveTurn = 0D;
                    }

                    accumulatedSign = sign;
                    accumulatedCurveTurn += absoluteTurn;

                    bool endsCurveIntoLongStraight = shortSegmentThreshold > 0D
                        && lengthA <= shortSegmentThreshold
                        && lengthB > shortSegmentThreshold;
                    if (endsCurveIntoLongStraight)
                    {
                        bendCount += IsD2AccumulatedTurn(accumulatedCurveTurn) ? 1 : 0;
                        accumulatedCurveTurn = 0D;
                        accumulatedSign = 0;
                    }
                }
                else
                {
                    bendCount += IsD2AccumulatedTurn(accumulatedCurveTurn) ? 1 : 0;
                    accumulatedCurveTurn = 0D;
                    accumulatedSign = 0;
                }
            }

            bendCount += IsD2AccumulatedTurn(accumulatedCurveTurn) ? 1 : 0;

            // 닫힌 철근은 TraceComponent가 [시작 ... 시작] 형태로 반환한다.
            // 기존 선형 루프는 중간 꼭짓점만 검사했기 때문에 시작/끝이 만나는 마지막 절곡 1개가
            // 누락될 수 있었다. 시작점의 이전=마지막 실제점, 다음=두 번째점으로 seam 각도를 별도 판정한다.
            if (isClosed)
            {
                ElongationPoint previous = points[points.Count - 2];
                ElongationPoint current = points[0];
                ElongationPoint next = points[1];
                double ax = current.X - previous.X;
                double ay = current.Y - previous.Y;
                double bx = next.X - current.X;
                double by = next.Y - current.Y;
                double lengthA = Math.Sqrt(ax * ax + ay * ay);
                double lengthB = Math.Sqrt(bx * bx + by * by);

                if (lengthA > 0.000001D && lengthB > 0.000001D)
                {
                    double seamTurn = Math.Abs(Math.Atan2(ax * by - ay * bx, ax * bx + ay * by) * 180D / Math.PI);
                    if (seamTurn >= D2MinimumAngle && seamTurn <= D2MaximumAngle)
                    {
                        bendCount++;
                    }
                }
            }

            return bendCount;
        }

        private static bool IsD2AccumulatedTurn(double angle)
        {
            return angle >= D2MinimumAngle && angle <= D2MaximumAngle;
        }

        private sealed class ElongationGraph
        {
            private readonly List<ElongationGraphVertex> vertices;
            private readonly List<ElongationGraphEdge> edges;

            private ElongationGraph(List<ElongationGraphVertex> vertices, List<ElongationGraphEdge> edges)
            {
                this.vertices = vertices;
                this.edges = edges;
            }

            public static ElongationGraph Build(List<ElongationSegment> segments, double tolerance)
            {
                List<ElongationGraphVertex> vertices = new List<ElongationGraphVertex>();
                List<ElongationGraphEdge> edges = new List<ElongationGraphEdge>();

                for (int i = 0; i < segments.Count; i++)
                {
                    int a = FindOrAddVertex(vertices, segments[i].A, tolerance);
                    int b = FindOrAddVertex(vertices, segments[i].B, tolerance);
                    if (a == b)
                    {
                        continue;
                    }

                    ElongationGraphEdge edge = new ElongationGraphEdge(a, b, segments[i].Length);
                    int edgeIndex = edges.Count;
                    edges.Add(edge);
                    vertices[a].Edges.Add(edgeIndex);
                    vertices[b].Edges.Add(edgeIndex);
                }

                return new ElongationGraph(vertices, edges);
            }

            public List<int> FindPrimaryRebarComponent()
            {
                List<int> best = new List<int>();
                double bestScore = Double.MinValue;
                bool[] visited = new bool[edges.Count];

                for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
                {
                    if (visited[edgeIndex])
                    {
                        continue;
                    }

                    List<int> component = new List<int>();
                    Queue<int> queue = new Queue<int>();
                    queue.Enqueue(edgeIndex);
                    visited[edgeIndex] = true;
                    double totalLength = 0D;

                    while (queue.Count > 0)
                    {
                        int currentEdge = queue.Dequeue();
                        component.Add(currentEdge);
                        totalLength += edges[currentEdge].Length;
                        int[] endpoints = new int[] { edges[currentEdge].A, edges[currentEdge].B };

                        for (int e = 0; e < endpoints.Length; e++)
                        {
                            List<int> connected = vertices[endpoints[e]].Edges;
                            for (int j = 0; j < connected.Count; j++)
                            {
                                int next = connected[j];
                                if (!visited[next])
                                {
                                    visited[next] = true;
                                    queue.Enqueue(next);
                                }
                            }
                        }
                    }

                    int degreeOneCount = CountDegreeOneVertices(component);
                    bool smoothClosedLoop = IsSmoothClosedLoop(component);
                    if (smoothClosedLoop)
                    {
                        // 원/타원처럼 닫힌 부드러운 곡선은 절곡 철근으로 선택하지 않는다.
                        continue;
                    }

                    double score = totalLength + (degreeOneCount >= 2 ? totalLength * 2D : 0D);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = component;
                    }
                }

                return best;
            }

            public List<ElongationPoint> TraceComponent(List<int> componentEdges)
            {
                List<ElongationPoint> points = new List<ElongationPoint>();
                if (componentEdges == null || componentEdges.Count == 0)
                {
                    return points;
                }

                HashSet<int> component = new HashSet<int>(componentEdges);
                Dictionary<int, int> degree = BuildComponentDegrees(component);
                int startVertex = -1;

                foreach (KeyValuePair<int, int> pair in degree)
                {
                    if (pair.Value == 1)
                    {
                        startVertex = pair.Key;
                        break;
                    }
                }

                if (startVertex < 0)
                {
                    startVertex = edges[componentEdges[0]].A;
                }

                HashSet<int> usedEdges = new HashSet<int>();
                int currentVertex = startVertex;
                int previousVertex = -1;
                points.Add(vertices[currentVertex].Point);

                while (usedEdges.Count < component.Count)
                {
                    int nextEdge = SelectNextEdge(currentVertex, previousVertex, component, usedEdges);
                    if (nextEdge < 0)
                    {
                        break;
                    }

                    usedEdges.Add(nextEdge);
                    int nextVertex = edges[nextEdge].A == currentVertex ? edges[nextEdge].B : edges[nextEdge].A;
                    previousVertex = currentVertex;
                    currentVertex = nextVertex;
                    points.Add(vertices[currentVertex].Point);

                    if (currentVertex == startVertex && usedEdges.Count == component.Count)
                    {
                        break;
                    }
                }

                return points;
            }

            private int SelectNextEdge(int currentVertex, int previousVertex, HashSet<int> component, HashSet<int> usedEdges)
            {
                List<int> candidates = new List<int>();
                List<int> connected = vertices[currentVertex].Edges;
                for (int i = 0; i < connected.Count; i++)
                {
                    int edgeIndex = connected[i];
                    if (component.Contains(edgeIndex) && !usedEdges.Contains(edgeIndex))
                    {
                        candidates.Add(edgeIndex);
                    }
                }

                if (candidates.Count == 0)
                {
                    return -1;
                }

                if (candidates.Count == 1 || previousVertex < 0)
                {
                    return candidates[0];
                }

                ElongationPoint previousPoint = vertices[previousVertex].Point;
                ElongationPoint currentPoint = vertices[currentVertex].Point;
                double inX = currentPoint.X - previousPoint.X;
                double inY = currentPoint.Y - previousPoint.Y;
                int bestEdge = candidates[0];
                double bestTurn = Double.MaxValue;

                for (int i = 0; i < candidates.Count; i++)
                {
                    int otherVertex = edges[candidates[i]].A == currentVertex ? edges[candidates[i]].B : edges[candidates[i]].A;
                    ElongationPoint nextPoint = vertices[otherVertex].Point;
                    double outX = nextPoint.X - currentPoint.X;
                    double outY = nextPoint.Y - currentPoint.Y;
                    double turn = Math.Abs(Math.Atan2(inX * outY - inY * outX, inX * outX + inY * outY));
                    if (turn < bestTurn)
                    {
                        bestTurn = turn;
                        bestEdge = candidates[i];
                    }
                }

                return bestEdge;
            }

            private int CountDegreeOneVertices(List<int> componentEdges)
            {
                Dictionary<int, int> degrees = BuildComponentDegrees(new HashSet<int>(componentEdges));
                int count = 0;
                foreach (KeyValuePair<int, int> pair in degrees)
                {
                    if (pair.Value == 1) count++;
                }
                return count;
            }

            private bool IsSmoothClosedLoop(List<int> componentEdges)
            {
                HashSet<int> component = new HashSet<int>(componentEdges);
                Dictionary<int, int> degrees = BuildComponentDegrees(component);
                if (degrees.Count < 6)
                {
                    return false;
                }

                foreach (KeyValuePair<int, int> pair in degrees)
                {
                    if (pair.Value != 2)
                    {
                        return false;
                    }
                }

                List<ElongationPoint> points = TraceComponent(componentEdges);
                if (points.Count < 7)
                {
                    return false;
                }

                double totalTurn = 0D;
                double maxLocalTurn = 0D;
                for (int i = 1; i < points.Count - 1; i++)
                {
                    double ax = points[i].X - points[i - 1].X;
                    double ay = points[i].Y - points[i - 1].Y;
                    double bx = points[i + 1].X - points[i].X;
                    double by = points[i + 1].Y - points[i].Y;
                    double turn = Math.Abs(Math.Atan2(ax * by - ay * bx, ax * bx + ay * by) * 180D / Math.PI);
                    totalTurn += turn;
                    maxLocalTurn = Math.Max(maxLocalTurn, turn);
                }

                return maxLocalTurn < 30D && totalTurn >= 300D;
            }

            private Dictionary<int, int> BuildComponentDegrees(HashSet<int> component)
            {
                Dictionary<int, int> result = new Dictionary<int, int>();
                foreach (int edgeIndex in component)
                {
                    Increment(result, edges[edgeIndex].A);
                    Increment(result, edges[edgeIndex].B);
                }
                return result;
            }

            private static void Increment(Dictionary<int, int> values, int key)
            {
                int current;
                values.TryGetValue(key, out current);
                values[key] = current + 1;
            }

            private static int FindOrAddVertex(List<ElongationGraphVertex> vertices, ElongationPoint point, double tolerance)
            {
                for (int i = 0; i < vertices.Count; i++)
                {
                    if (vertices[i].Point.DistanceTo(point) <= tolerance)
                    {
                        return i;
                    }
                }

                vertices.Add(new ElongationGraphVertex(point));
                return vertices.Count - 1;
            }
        }

        private sealed class ElongationGraphVertex
        {
            public readonly ElongationPoint Point;
            public readonly List<int> Edges = new List<int>();

            public ElongationGraphVertex(ElongationPoint point)
            {
                Point = point;
            }
        }

        private sealed class ElongationGraphEdge
        {
            public readonly int A;
            public readonly int B;
            public readonly double Length;

            public ElongationGraphEdge(int a, int b, double length)
            {
                A = a;
                B = b;
                Length = length;
            }
        }

        private sealed class LooseEndpoint
        {
            public readonly int SegmentIndex;
            public readonly ElongationPoint Point;
            public readonly ElongationPoint Inside;

            public LooseEndpoint(int segmentIndex, ElongationPoint point, ElongationPoint inside)
            {
                SegmentIndex = segmentIndex;
                Point = point;
                Inside = inside;
            }
        }

        private sealed class ElongationSegment
        {
            public readonly ElongationPoint A;
            public readonly ElongationPoint B;
            public readonly double Length;

            public ElongationSegment(ElongationPoint a, ElongationPoint b, double length)
            {
                A = a;
                B = b;
                Length = length;
            }
        }

        private struct ElongationPoint
        {
            public readonly double X;
            public readonly double Y;

            public ElongationPoint(double x, double y)
            {
                X = x;
                Y = y;
            }

            public double DistanceTo(ElongationPoint other)
            {
                double dx = X - other.X;
                double dy = Y - other.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }
        }
    }

    public sealed class RebarElongationResult
    {
        public double OriginalLengthMm { get; set; }
        public int DiameterMm { get; set; }
        public int BendCount { get; set; }
        public double DeductionMm { get; set; }
        public double FinalLengthMm { get; set; }
    }
}
