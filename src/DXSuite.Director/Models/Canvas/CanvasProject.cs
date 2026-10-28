namespace DXSuite.Director.Models.Canvas
{
    public class CanvasProject
    {
        public double CanvasWidth { get; set; }
        public double CanvasHeight { get; set; }
        public int LocationId { get; set; }
        public int? ConstraintId { get; set; }
        public List<CanvasElement> Elements { get; set; } = new List<CanvasElement>();
        public CanvasBackground Background { get; set; }

        public CanvasProject(double width, double height, int locationId, int? constraintId = null)
        {
            CanvasWidth = width;
            CanvasHeight = height;
            LocationId = locationId;
            ConstraintId = constraintId;
            Background = new CanvasBackground(width, height);
            Elements.Add(Background);
        }

        public void AddElement(CanvasElement element)
        {
            element.ZIndex = Elements.Count;
            Elements.Add(element);
        }

        public void RemoveElement(string elementId)
        {
            Elements.RemoveAll(e => e.Id == elementId && e != Background);
            ReorderZIndexes();
        }

        public void MoveElement(string elementId, int newIndex)
        {
            var element = Elements.FirstOrDefault(e => e.Id == elementId && e != Background);
            if (element != null)
            {
                Elements.Remove(element);
                Elements.Insert(Math.Max(1, newIndex), element); // Keep background at index 0
                ReorderZIndexes();
            }
        }

        private void ReorderZIndexes()
        {
            for (int i = 0; i < Elements.Count; i++)
            {
                Elements[i].ZIndex = i;
            }
        }

        public List<CanvasElement> GetElementsOrderedByZIndex()
        {
            return Elements.OrderBy(e => e.ZIndex).ToList();
        }

        public List<CanvasElement> GetElementsOrderedByZIndex(bool includeBackground = true)
        {
            var elements = Elements.OrderBy(e => e.ZIndex).AsEnumerable();
            if (!includeBackground)
            {
                elements = elements.Where(e => e is not CanvasBackground);
            }
            return elements.ToList();
        }
    }
}
