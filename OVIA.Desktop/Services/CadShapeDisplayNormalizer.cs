using System;
using System.Collections.Generic;
using System.Globalization;

namespace OVIA.Desktop
{
    /// <summary>
    /// BarList·ERP·출력·형상 확인/수정 팝업에서 공통으로 사용하는 CAD 형상 표시 준비 단계입니다.
    ///
    /// 중요 원칙:
    /// - CAD에 그려진 LINE/ARC/CIRCLE의 좌표와 종횡비를 그대로 유지합니다.
    /// - TEXT에 기록된 치수값을 해석해 형상선 길이를 다시 계산하지 않습니다.
    /// - 구간별 길이를 균등화하거나 제곱근·로그 방식으로 압축하지 않습니다.
    /// - 셀 크기 맞춤은 CadShapeRenderer가 전체 형상 bounds에 단일 배율을 적용해 처리합니다.
    /// - 동적 블록의 비활성 가시성 상태에서 유입된 겹침 치수 사본은 목록과 편집창에서 같은 규칙으로 제거합니다.
    ///
    /// 원본 객체의 우발적 변경을 막기 위해 항상 깊은 복사본을 반환합니다.
    /// </summary>
    internal static class CadShapeDisplayNormalizer
    {
        private const double MinimumTextBounds = 0.1D;
        private const double StrongOverlapAreaRatio = 0.50D;
        private const double StrongOverlapCenterDistanceRatio = 0.70D;

        public static CadShapeData CreateDisplayData(CadShapeData source)
        {
            CadShapeData copy = CloneData(source);

            if (copy != null && IsCadDerivedData(copy))
            {
                RemoveOverlappingGhostDimensionTexts(copy.Elements, copy.Width, copy.Height);
            }

            return copy;
        }

        private static bool IsCadDerivedData(CadShapeData data)
        {
            if (data == null)
            {
                return false;
            }

            string source = data.Source == null ? "" : data.Source.Trim();

            return !source.Equals("OVIA_MANUAL", StringComparison.OrdinalIgnoreCase)
                && !source.Equals("MANUAL", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 형상 확인·수정 팝업용 문서를 BarList 렌더링과 동일한 겹침 문자 정리 규칙으로 준비합니다.
        /// CAD 원본 및 CAD에서 파생된 OVIA_EDIT 문서만 정리하며 수동 생성 문서는 그대로 보존합니다.
        /// </summary>
        public static CadShapeEditDocument CreateEditableDocument(CadShapeEditDocument source)
        {
            if (source == null)
            {
                return CadShapeEditDocument.CreateEmpty();
            }

            CadShapeEditDocument copy = source.Clone();

            if (IsCadDerivedDocument(copy))
            {
                RemoveOverlappingGhostDimensionTexts(copy.Elements, copy.Width, copy.Height);
            }

            copy.EnsureTextIds();
            return copy;
        }

        private static bool IsCadDerivedDocument(CadShapeEditDocument document)
        {
            if (document == null)
            {
                return false;
            }

            string source = document.Source == null ? "" : document.Source.Trim();

            if (source.StartsWith("CAD", StringComparison.OrdinalIgnoreCase)
                || source.Equals("OVIA_EDIT", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return document.OriginalSourcePath != null && document.OriginalSourcePath.Trim() != "";
        }

        private static CadShapeData CloneData(CadShapeData source)
        {
            if (source == null)
            {
                return null;
            }

            CadShapeData copy = new CadShapeData();
            copy.Version = source.Version;
            copy.Source = source.Source;
            copy.LayoutPolicy = source.LayoutPolicy;
            copy.Width = source.Width;
            copy.Height = source.Height;

            if (source.Elements == null)
            {
                return copy;
            }

            int i;

            for (i = 0; i < source.Elements.Count; i++)
            {
                CadShapeElement item = source.Elements[i];

                if (item == null)
                {
                    copy.Elements.Add(null);
                    continue;
                }

                CadShapeElement cloned = new CadShapeElement();
                cloned.Type = item.Type;
                cloned.Text = item.Text;
                cloned.TextId = item.TextId;
                cloned.X1 = item.X1;
                cloned.Y1 = item.Y1;
                cloned.X2 = item.X2;
                cloned.Y2 = item.Y2;
                cloned.CX = item.CX;
                cloned.CY = item.CY;
                cloned.Radius = item.Radius;
                cloned.StartAngle = item.StartAngle;
                cloned.EndAngle = item.EndAngle;
                cloned.Height = item.Height;
                cloned.TextScale = item.TextScale;
                cloned.Rotation = item.Rotation;
                cloned.HasBounds = item.HasBounds;
                cloned.BoundsMinX = item.BoundsMinX;
                cloned.BoundsMinY = item.BoundsMinY;
                cloned.BoundsMaxX = item.BoundsMaxX;
                cloned.BoundsMaxY = item.BoundsMaxY;
                copy.Elements.Add(cloned);
            }

            return copy;
        }

        private static void RemoveOverlappingGhostDimensionTexts(
            List<CadShapeElement> elements,
            double width,
            double height)
        {
            if (elements == null || elements.Count < 2)
            {
                return;
            }

            List<GhostTextCandidate> candidates = new List<GhostTextCandidate>();
            int i;

            for (i = 0; i < elements.Count; i++)
            {
                CadShapeElement item = elements[i];

                if (item == null || !String.Equals(item.Type, "TEXT", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                GhostTextCandidate candidate = BuildCandidate(
                    i,
                    item.Text,
                    item.X1,
                    item.Y1,
                    item.Height,
                    item.TextScale,
                    item.HasBounds,
                    item.BoundsMinX,
                    item.BoundsMinY,
                    item.BoundsMaxX,
                    item.BoundsMaxY,
                    width,
                    height
                );

                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            }

            List<int> removeIndexes = FindOverlappingGhostDimensionTextIndexes(candidates);

            for (i = removeIndexes.Count - 1; i >= 0; i--)
            {
                elements.RemoveAt(removeIndexes[i]);
            }
        }

        private static void RemoveOverlappingGhostDimensionTexts(
            List<CadShapeEditElement> elements,
            double width,
            double height)
        {
            if (elements == null || elements.Count < 2)
            {
                return;
            }

            List<GhostTextCandidate> candidates = new List<GhostTextCandidate>();
            int i;

            for (i = 0; i < elements.Count; i++)
            {
                CadShapeEditElement item = elements[i];

                if (item == null || !String.Equals(item.Type, "TEXT", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                GhostTextCandidate candidate = BuildCandidate(
                    i,
                    item.Text,
                    item.X1,
                    item.Y1,
                    item.Height,
                    item.TextScale,
                    item.HasBounds,
                    item.BoundsMinX,
                    item.BoundsMinY,
                    item.BoundsMaxX,
                    item.BoundsMaxY,
                    width,
                    height
                );

                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            }

            List<int> removeIndexes = FindOverlappingGhostDimensionTextIndexes(candidates);

            for (i = removeIndexes.Count - 1; i >= 0; i--)
            {
                elements.RemoveAt(removeIndexes[i]);
            }
        }

        private static GhostTextCandidate BuildCandidate(
            int sourceIndex,
            string text,
            double x,
            double y,
            double height,
            double textScale,
            bool hasBounds,
            double boundsMinX,
            double boundsMinY,
            double boundsMaxX,
            double boundsMaxY,
            double cellWidth,
            double cellHeight)
        {
            string kind;
            decimal value;

            if (!TryParseDimensionText(text, out kind, out value))
            {
                return null;
            }

            GhostTextCandidate candidate = new GhostTextCandidate();
            candidate.SourceIndex = sourceIndex;
            candidate.Kind = kind;
            candidate.Value = value;

            if (hasBounds && boundsMaxX > boundsMinX && boundsMaxY > boundsMinY)
            {
                candidate.MinX = boundsMinX;
                candidate.MinY = boundsMinY;
                candidate.MaxX = boundsMaxX;
                candidate.MaxY = boundsMaxY;
            }
            else
            {
                double safeScale = Math.Max(textScale, 0.25D);
                double estimatedHeight = Math.Max(
                    Math.Max(height, MinimumTextBounds) * safeScale,
                    Math.Max(Math.Min(Math.Abs(cellWidth), Math.Abs(cellHeight)) * 0.015D, MinimumTextBounds)
                );
                double estimatedWidth = Math.Max(
                    estimatedHeight * 0.55D * Math.Max(text == null ? 0 : text.Trim().Length, 1),
                    estimatedHeight
                );

                candidate.MinX = x - estimatedWidth / 2D;
                candidate.MaxX = x + estimatedWidth / 2D;
                candidate.MinY = y - estimatedHeight / 2D;
                candidate.MaxY = y + estimatedHeight / 2D;
            }

            return candidate;
        }

        private static List<int> FindOverlappingGhostDimensionTextIndexes(List<GhostTextCandidate> candidates)
        {
            List<int> result = new List<int>();

            if (candidates == null || candidates.Count < 2)
            {
                return result;
            }

            HashSet<int> visited = new HashSet<int>();
            HashSet<int> removeSourceIndexes = new HashSet<int>();
            int i;

            for (i = 0; i < candidates.Count; i++)
            {
                if (visited.Contains(i))
                {
                    continue;
                }

                List<int> cluster = new List<int>();
                Queue<int> queue = new Queue<int>();
                queue.Enqueue(i);
                visited.Add(i);

                while (queue.Count > 0)
                {
                    int currentIndex = queue.Dequeue();
                    cluster.Add(currentIndex);
                    int compareIndex;

                    for (compareIndex = 0; compareIndex < candidates.Count; compareIndex++)
                    {
                        if (visited.Contains(compareIndex))
                        {
                            continue;
                        }

                        if (AreStronglyOverlapping(candidates[currentIndex], candidates[compareIndex]))
                        {
                            visited.Add(compareIndex);
                            queue.Enqueue(compareIndex);
                        }
                    }
                }

                if (cluster.Count < 2)
                {
                    continue;
                }

                HashSet<int> clusterSet = new HashSet<int>(cluster);
                List<int> outsideDuplicatedClusterItems = new List<int>();
                int clusterPosition;

                for (clusterPosition = 0; clusterPosition < cluster.Count; clusterPosition++)
                {
                    int clusterIndex = cluster[clusterPosition];

                    if (HasIndependentOutsideCopy(clusterIndex, clusterSet, candidates))
                    {
                        outsideDuplicatedClusterItems.Add(clusterIndex);
                    }
                }

                /*
                 * 겹침 컴포넌트 안에 정상 130°/135°와 숨김 91°/74°가 함께 연결될 수 있습니다.
                 * 컴포넌트 전체를 삭제하지 않고, 외부 정상 사본이 확인된 후보끼리 실제로 겹치는
                 * 2개 이상의 하위 군집만 제거합니다. 따라서 외부 사본이 없는 정상 치수는 보존됩니다.
                 */
                if (outsideDuplicatedClusterItems.Count < 2)
                {
                    continue;
                }

                HashSet<int> duplicatedVisited = new HashSet<int>();
                int duplicatedPosition;

                for (duplicatedPosition = 0; duplicatedPosition < outsideDuplicatedClusterItems.Count; duplicatedPosition++)
                {
                    int duplicatedSeed = outsideDuplicatedClusterItems[duplicatedPosition];

                    if (duplicatedVisited.Contains(duplicatedSeed))
                    {
                        continue;
                    }

                    List<int> duplicatedSubCluster = new List<int>();
                    Queue<int> duplicatedQueue = new Queue<int>();
                    duplicatedQueue.Enqueue(duplicatedSeed);
                    duplicatedVisited.Add(duplicatedSeed);

                    while (duplicatedQueue.Count > 0)
                    {
                        int duplicatedCurrent = duplicatedQueue.Dequeue();
                        duplicatedSubCluster.Add(duplicatedCurrent);
                        int duplicatedComparePosition;

                        for (duplicatedComparePosition = 0; duplicatedComparePosition < outsideDuplicatedClusterItems.Count; duplicatedComparePosition++)
                        {
                            int duplicatedCompare = outsideDuplicatedClusterItems[duplicatedComparePosition];

                            if (duplicatedVisited.Contains(duplicatedCompare))
                            {
                                continue;
                            }

                            if (AreStronglyOverlapping(candidates[duplicatedCurrent], candidates[duplicatedCompare]))
                            {
                                duplicatedVisited.Add(duplicatedCompare);
                                duplicatedQueue.Enqueue(duplicatedCompare);
                            }
                        }
                    }

                    if (duplicatedSubCluster.Count < 2)
                    {
                        continue;
                    }

                    int removePosition;

                    for (removePosition = 0; removePosition < duplicatedSubCluster.Count; removePosition++)
                    {
                        removeSourceIndexes.Add(candidates[duplicatedSubCluster[removePosition]].SourceIndex);
                    }
                }
            }

            result.AddRange(removeSourceIndexes);
            result.Sort();
            return result;
        }


        private static bool HasIndependentOutsideCopy(
            int targetIndex,
            HashSet<int> clusterSet,
            List<GhostTextCandidate> candidates)
        {
            if (candidates == null
                || clusterSet == null
                || targetIndex < 0
                || targetIndex >= candidates.Count)
            {
                return false;
            }

            GhostTextCandidate target = candidates[targetIndex];
            int outsideIndex;

            for (outsideIndex = 0; outsideIndex < candidates.Count; outsideIndex++)
            {
                if (clusterSet.Contains(outsideIndex))
                {
                    continue;
                }

                if (DimensionValuesEqual(target, candidates[outsideIndex])
                    && IsSpatiallyIsolated(outsideIndex, candidates))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSpatiallyIsolated(int targetIndex, List<GhostTextCandidate> candidates)
        {
            if (candidates == null || targetIndex < 0 || targetIndex >= candidates.Count)
            {
                return false;
            }

            int i;

            for (i = 0; i < candidates.Count; i++)
            {
                if (i == targetIndex)
                {
                    continue;
                }

                if (AreStronglyOverlapping(candidates[targetIndex], candidates[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreStronglyOverlapping(GhostTextCandidate first, GhostTextCandidate second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            double intersectionWidth = Math.Min(first.MaxX, second.MaxX) - Math.Max(first.MinX, second.MinX);
            double intersectionHeight = Math.Min(first.MaxY, second.MaxY) - Math.Max(first.MinY, second.MinY);
            double firstWidth = Math.Max(first.MaxX - first.MinX, MinimumTextBounds);
            double firstHeight = Math.Max(first.MaxY - first.MinY, MinimumTextBounds);
            double secondWidth = Math.Max(second.MaxX - second.MinX, MinimumTextBounds);
            double secondHeight = Math.Max(second.MaxY - second.MinY, MinimumTextBounds);

            if (intersectionWidth > 0D && intersectionHeight > 0D)
            {
                double intersectionArea = intersectionWidth * intersectionHeight;
                double smallerArea = Math.Min(firstWidth * firstHeight, secondWidth * secondHeight);

                if (smallerArea > 0.000001D && intersectionArea / smallerArea >= StrongOverlapAreaRatio)
                {
                    return true;
                }
            }

            double firstCenterX = (first.MinX + first.MaxX) / 2D;
            double firstCenterY = (first.MinY + first.MaxY) / 2D;
            double secondCenterX = (second.MinX + second.MaxX) / 2D;
            double secondCenterY = (second.MinY + second.MaxY) / 2D;
            double deltaX = firstCenterX - secondCenterX;
            double deltaY = firstCenterY - secondCenterY;
            double centerDistance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            double textScale = Math.Max(Math.Min(firstHeight, secondHeight), MinimumTextBounds);

            return centerDistance <= Math.Max(textScale * StrongOverlapCenterDistanceRatio, 0.01D);
        }

        private static bool DimensionValuesEqual(GhostTextCandidate left, GhostTextCandidate right)
        {
            if (left == null || right == null || !String.Equals(left.Kind, right.Kind, StringComparison.Ordinal))
            {
                return false;
            }

            return Decimal.Round(left.Value, 3, MidpointRounding.AwayFromZero)
                == Decimal.Round(right.Value, 3, MidpointRounding.AwayFromZero);
        }

        private static bool TryParseDimensionText(string text, out string kind, out decimal value)
        {
            kind = "";
            value = 0M;

            if (text == null)
            {
                return false;
            }

            string normalized = text.Trim();

            if (normalized == "")
            {
                return false;
            }

            normalized = normalized.Replace("%%D", "°").Replace("%%d", "°").Replace("˚", "°").Replace("º", "°");
            bool isAngle = normalized.EndsWith("°", StringComparison.Ordinal);

            if (isAngle)
            {
                normalized = normalized.Substring(0, normalized.Length - 1).Trim();
            }

            normalized = normalized.Replace(",", "");

            if (normalized == "")
            {
                return false;
            }

            if (!Decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
                && !Decimal.TryParse(normalized, out value))
            {
                return false;
            }

            kind = isAngle ? "ANGLE" : "NUMBER";
            return true;
        }

        private sealed class GhostTextCandidate
        {
            public int SourceIndex;
            public string Kind = "";
            public decimal Value;
            public double MinX;
            public double MinY;
            public double MaxX;
            public double MaxY;
        }
    }
}
