using System;

namespace OVIA.Desktop
{
    /// <summary>
    /// BarList·ERP·출력용 형상 데이터의 표시 준비 단계입니다.
    ///
    /// 중요 원칙:
    /// - CAD에 그려진 LINE/ARC/CIRCLE의 좌표와 종횡비를 그대로 유지합니다.
    /// - TEXT에 기록된 치수값을 해석해 형상선 길이를 다시 계산하지 않습니다.
    /// - 구간별 길이를 균등화하거나 제곱근·로그 방식으로 압축하지 않습니다.
    /// - 셀 크기 맞춤은 CadShapeRenderer가 전체 형상 bounds에 단일 배율을 적용해 처리합니다.
    ///
    /// 이 클래스는 원본 객체의 우발적 변경을 막기 위한 깊은 복사만 수행합니다.
    /// </summary>
    internal static class CadShapeDisplayNormalizer
    {
        public static CadShapeData CreateDisplayData(CadShapeData source)
        {
            return CloneData(source);
        }

        private static CadShapeData CloneData(CadShapeData source)
        {
            if (source == null)
            {
                return null;
            }

            CadShapeData copy = new CadShapeData();
            copy.Version = source.Version;
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
    }
}
