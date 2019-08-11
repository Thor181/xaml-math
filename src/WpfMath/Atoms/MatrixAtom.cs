using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WpfMath.Boxes;

namespace WpfMath.Atoms
{
    /// <summary>An atom representing a tabular arrangement of atoms.</summary>
    internal class MatrixAtom : Atom
    {
        public MatrixAtom(
            SourceSpan source,
            List<List<Atom>> cells,
            MatrixCellAlignment matrixCellAlignment = MatrixCellAlignment.Center,
            double verticalPadding = 0.35,
            double horizontalPadding = 0.35) : base(source)
        {
            MatrixCells = new ReadOnlyCollection<ReadOnlyCollection<Atom>>(
                cells.Select(row => new ReadOnlyCollection<Atom>(row)).ToList());
            MatrixCellAlignment = matrixCellAlignment;
            VerticalPadding = verticalPadding;
            HorizontalPadding = horizontalPadding;
        }

        public ReadOnlyCollection<ReadOnlyCollection<Atom>> MatrixCells { get; }

        public double VerticalPadding { get; }

        public double HorizontalPadding { get; }

        public MatrixCellAlignment MatrixCellAlignment { get; }

        protected override Box CreateBoxCore(TexEnvironment environment)
        {
            var rowCount = MatrixCells.Count;
            var maxColumnCount = MatrixCells.Max(row => row.Count);

            var rowBoxes = new List<HorizontalBox>();
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                var rowBox = new HorizontalBox();
                rowBoxes.Add(rowBox);

                // TODO[F]: Get rid of rowBoxes, rowIndex, columnIndex

                var columnIndex = 0;
                foreach (var rowItem in CreateRowCellBoxes(environment, rowIndex, maxColumnCount))
                {
                    //cell left pad
                    var cellleftpad = new StrutBox(HorizontalPadding / 2, 0, 0, 0)
                    {
                        Tag = $"CellLeftPad{rowIndex}:{columnIndex}",
                        Shift = 0,
                    };

                    //cell right pad
                    var cellrightpad = new StrutBox(HorizontalPadding / 2, 0, 0, 0)
                    {
                        Tag = $"CellRightPad{rowIndex}:{columnIndex}",
                        Shift = 0,
                    };

                    //cell box holder
                    var rowcolbox = new VerticalBox() {Tag = $"Cell{rowIndex}:{columnIndex}"};

                    var celltoppad = new StrutBox(rowItem.TotalWidth, VerticalPadding / 2, 0, 0) {Tag = $"CellTopPad{rowIndex}:{columnIndex}"};
                    var cellbottompad = new StrutBox(rowItem.TotalWidth, VerticalPadding / 2, 0, 0)
                        {Tag = $"CellBottomPad{rowIndex}:{columnIndex}"};

                    rowcolbox.Add(celltoppad);
                    rowcolbox.Add(rowItem);
                    rowcolbox.Add(cellbottompad);

                    rowBox.Add(cellleftpad);
                    rowBox.Add(rowcolbox);
                    rowBox.Add(cellrightpad);

                    ++columnIndex;
                }
            }

            var (rowHeights, columnWidths) = CalculateDimensions(rowBoxes, maxColumnCount);
            var matrixCellGaps = CalculateCellGaps(rowBoxes, rowHeights, columnWidths);

            ApplyCellSizes(rowBoxes, matrixCellGaps);

            var rowsContainer = new VerticalBox();
            foreach (var row in rowBoxes)
            {
                rowsContainer.Add(row);
            }

            var height = rowsContainer.TotalHeight;
            rowsContainer.Depth = height / 2;
            rowsContainer.Height = height / 2;

            return rowsContainer;
        }

        private IEnumerable<Box> CreateRowCellBoxes(
            TexEnvironment environment,
            int rowIndex,
            int maxColumnCount)
        {
            // TODO[F]: remove the rowIndex parameter altogether with Tags
            for (int j = 0; j < maxColumnCount; j++)
            {
                var rowCellBox = MatrixCells[rowIndex][j] == null
                    ? StrutBox.Empty
                    : MatrixCells[rowIndex][j].CreateBox(environment);
                rowCellBox.Tag = "innercell"; // TODO[F]: This is dangerous: we may set the value for the cachec StrutBox.Empty
                yield return rowCellBox;
            }
        }

        /// <summary>
        /// Calculates the height of each row and the width of each column and returns arrays of those.
        /// </summary>
        private (double[] RowHeights, double[] ColumnWidths) CalculateDimensions(
            List<HorizontalBox> matrix,
            int columnCount)
        {
            var rowHeights = matrix.Select(row => row.TotalHeight).ToArray();
            var columnWidths = Enumerable.Range(0, columnCount)
                .Select(column => matrix.Select(row => row.Children[1 + 3 * column]).Max(cell => cell.TotalWidth))
                .ToArray();
            return (rowHeights, columnWidths);
        }

        private static List<List<CellGaps>> CalculateCellGaps(
            IEnumerable<HorizontalBox> rows,
            double[] rowHeights,
            double[] columnWidths)
        {
            // TODO[F]: Pass here only the right boxes collection
            var rowIndex = 0;
            var columns = 0;

            var matrixCellGaps = new List<List<CellGaps>>();
            foreach (var row in rows)
            {
                var rowGaps = new List<CellGaps>();

                for (int j = 0; j < row.Children.Count; j++)
                {
                    var rowcolitem = row.Children[j];
                    if (rowcolitem is VerticalBox && rowcolitem.Tag.ToString() == $"Cell{rowIndex}:{columns}")
                    {
                        // TODO[F]: require the vertical box list as input
                        double cellVShift = rowHeights[rowIndex] - rowcolitem.TotalHeight;

                        double cellHShift = columnWidths[columns] - rowcolitem.TotalWidth;
                        row.Children[j - 1].Shift = rowcolitem.Depth;
                        row.Children[j + 1].Shift = rowcolitem.Depth;
                        rowGaps.Add(new CellGaps {Horizontal = cellHShift / 2, Vertical = cellVShift / 2});

                        columns++;
                    }
                }

                columns = 0;
                rowIndex++;
                matrixCellGaps.Add(rowGaps);
            }

            return matrixCellGaps;
        }

        private void ApplyCellSizes(List<HorizontalBox> rowBoxes, List<List<CellGaps>> matrixCellGaps)
        {
            var rows = 0;
            var columns = 0;
            foreach (var row in rowBoxes)
            {
                double rowwidth = 0;
                for (int j = 0; j < row.Children.Count; j++)
                {
                    var currowcolitem = row.Children[j];
                    var prevrowcolitem = j > 0 ? row.Children[j - 1] : row.Children[j];
                    var nextrowcolitem = j < row.Children.Count - 1
                        ? row.Children[j + 1]
                        : row.Children[j];

                    if (currowcolitem is VerticalBox && Regex.IsMatch(currowcolitem.Tag.ToString(), @"Cell[0-9]+:[0-9]+"))
                    {
                        rowwidth += currowcolitem.TotalWidth;
                        var leftstructboxtag = $"CellLeftPad{rows}:{columns}";
                        var rightstructboxtag = $"CellRightPad{rows}:{columns}";

                        switch (MatrixCellAlignment)
                        {
                            case MatrixCellAlignment.Left:
                            {
                                if (prevrowcolitem is StrutBox && prevrowcolitem.Tag.ToString() == leftstructboxtag)
                                {
                                    //prevrowcolitem.Width += MatrixCellGaps[a][b].Item1;
                                    rowwidth += prevrowcolitem.TotalWidth;
                                }

                                if (nextrowcolitem is StrutBox && nextrowcolitem.Tag.ToString() == rightstructboxtag)
                                {
                                    nextrowcolitem.Width += 2 * matrixCellGaps[rows][columns].Horizontal;
                                    rowwidth += nextrowcolitem.TotalWidth;
                                }

                                break;
                            }

                            case MatrixCellAlignment.Center:
                            {
                                if (prevrowcolitem is StrutBox && prevrowcolitem.Tag.ToString() == leftstructboxtag)
                                {
                                    prevrowcolitem.Width += matrixCellGaps[rows][columns].Horizontal;
                                    rowwidth += prevrowcolitem.TotalWidth;
                                }

                                if (nextrowcolitem is StrutBox && nextrowcolitem.Tag.ToString() == rightstructboxtag)
                                {
                                    nextrowcolitem.Width += matrixCellGaps[rows][columns].Horizontal;
                                    rowwidth += nextrowcolitem.TotalWidth;
                                }

                                break;
                            }
                        }

                        double cellheight = 0;
                        //check the vertical cell gap size and increase appropriately
                        for (int k = 0; k < ((VerticalBox) currowcolitem).Children.Count; k++)
                        {
                            var curcellitem = ((VerticalBox) currowcolitem).Children[k];
                            var prevcellitem = k > 0
                                ? ((VerticalBox) currowcolitem).Children[k - 1]
                                : ((VerticalBox) currowcolitem).Children[k];
                            var nextcellitem = k < (((VerticalBox) currowcolitem).Children.Count - 1)
                                ? ((VerticalBox) currowcolitem).Children[k + 1]
                                : ((VerticalBox) currowcolitem).Children[k];

                            if (curcellitem.Tag.ToString() == "innercell")
                            {
                                cellheight += curcellitem.TotalHeight;
                                var topstructboxtag = $"CellTopPad{rows}:{columns}";
                                var bottomstructboxtag = $"CellBottomPad{rows}:{columns}";

                                if (prevcellitem.Tag.ToString() == topstructboxtag)
                                {
                                    prevcellitem.Height += matrixCellGaps[rows][columns].Vertical;
                                    //prevcellitem.Background = Brushes.Aquamarine;
                                    cellheight += prevcellitem.TotalHeight;
                                    if (prevcellitem.Height > (currowcolitem.Height / 2))
                                    {
                                    }
                                }

                                if (nextcellitem.Tag.ToString() == bottomstructboxtag)
                                {
                                    nextcellitem.Height += matrixCellGaps[rows][columns].Vertical;
                                    //nextcellitem.Background = Brushes.BurlyWood;
                                    cellheight += nextcellitem.TotalHeight;
                                }

                                if (prevrowcolitem is StrutBox && prevrowcolitem.Tag.ToString() == leftstructboxtag)
                                {
                                    prevrowcolitem.Shift += matrixCellGaps[rows][columns].Vertical;
                                }

                                if (nextrowcolitem is StrutBox && nextrowcolitem.Tag.ToString() == rightstructboxtag)
                                {
                                    nextrowcolitem.Shift += matrixCellGaps[rows][columns].Vertical;
                                }

                                //currowcolitem.Shift -= matrixCellGaps[a][b].Vertical; ;
                            }
                        }

                        //currowcolitem.Height = cellheight;
                        columns++;
                    }
                }

                row.Width = rowwidth;
                columns = 0;
                rows++;
            }
        }

        private struct CellGaps
        {
            public double Horizontal { get; set; }
            public double Vertical { get; set; }
        }
    }
}
