using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Create_AddHole
{
    [TransactionAttribute(TransactionMode.Manual)]
    public class AddHole : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document arDoc = commandData.Application.ActiveUIDocument.Document; // документ АР
            Document ovDoc = arDoc.Application.Documents.OfType<Document>().Where(x => x.Title.Contains("ОВ")).FirstOrDefault(); // документ ОВ
            if (ovDoc == null)			// если не удалось обнаружить файл ОВ
            {
                TaskDialog.Show("Ошибка", "Не найден ОВ файл");
                return Result.Cancelled;			// результат отмены
            }

            FamilySymbol familySymbol = new FilteredElementCollector(arDoc) // проверяем, загружено ли семейство в проект
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .OfType<FamilySymbol>()
                .Where(x => x.FamilyName.Equals("Отверстие"))
                .FirstOrDefault();

            if (familySymbol == null)			// проверяем, найдено ли семейство в проекте
            {
                TaskDialog.Show("Ошибка", "Не найдено семейство \"Отверстие\"");
                return Result.Cancelled;
            }

            List<Element> ducts = new FilteredElementCollector(ovDoc)  //собираем список воздуховодов в ОВ
                .OfClass(typeof(Duct))
                .OfType<Duct>()
                .Select(x => x as Element)
                .ToList();

            List<Element> pipes = new FilteredElementCollector(ovDoc) //собираем список труб в ОВ
                .OfClass(typeof(Pipe))
                .OfType<Pipe>()
                .Select(x => x as Element)
                .ToList();

            List<Element> elementsOV = new List<Element>(); // собираем в общий список воздуховоды и трубы
            elementsOV.AddRange(ducts);
            elementsOV.AddRange(pipes);

            View3D view3D = new FilteredElementCollector(arDoc) 	// получаем 3D вид
                .OfClass(typeof(View3D))
                .OfType<View3D>()
                .Where(x => !x.IsTemplate) 			// проверяем, что 3D вид не является шаблоном
                .FirstOrDefault();

            if (view3D == null)				// проверяем, найден ли 3D вид
            {
                TaskDialog.Show("Ошибка", "Не найден 3D вид");
                return Result.Cancelled;
            }

            ReferenceIntersector referenceIntersector = new ReferenceIntersector(new ElementClassFilter(typeof(Wall)), FindReferenceTarget.Element, view3D);

            Transaction tr = new Transaction(arDoc);
            tr.Start("Расстановка отверстий");

            if (!familySymbol.IsActive) 
            { familySymbol.Activate(); }        // активируем семейство
            tr.Commit();


            Transaction transaction = new Transaction(arDoc);
            transaction.Start("Расстановка отверстий");
            foreach (Element el in elementsOV)
            {
                Pipe pipe = el as Pipe;
                Duct duct = el as Duct;

                Line curve = pipe == null ? (duct.Location as LocationCurve).Curve as Line : (pipe.Location as LocationCurve).Curve as Line;	// линия curve воздуховода или трубы
                XYZ point = curve.GetEndPoint(0);	// начальная точка curve воздуховода/трубы
                XYZ direction = curve.Direction;	// направление воздуховода/трубы 


                List<ReferenceWithContext> intersections = referenceIntersector.Find(point, direction) // список пересечений
                    .Where(x => x.Proximity <= curve.Length)  //расстояние Proximity не превышает длину объекта
                    .Distinct(new ReferenceWithContextElementEqualityComparer())  //оставляет одно пересечение из двух идентичных
                    .ToList();

                foreach (ReferenceWithContext refer in intersections)
                {
                    double proximity = refer.Proximity;   // расстояние до объекта
                    Reference reference = refer.GetReference();   // ссылка 



                    Element host = arDoc.GetElement(reference.ElementId);  // получаем элемент по Id, используя полученную ссылку
                    Level level = arDoc.GetElement(host.LevelId) as Level;	// получаем уровень, на котором находится стена 

                    XYZ pointHole = point + (direction * proximity);  // точка вставки отверстия

                    FamilyInstance hole = arDoc.Create.NewFamilyInstance(pointHole, familySymbol, host, level, StructuralType.NonStructural);	// вставка отверстия в проект
                    Parameter width = hole.LookupParameter("Ширина");
                    Parameter height = hole.LookupParameter("Высота");
                    if (duct != null)
                    {
                        width.Set(duct.Diameter);    // устанавливаем ширину отверстия в соответствии с размером воздуховода
                        height.Set(duct.Diameter);   // устанавливаем высоту отверстия в соответствии с размером воздуховода

                    }
                    else if (pipe != null)
                    {
                        width.Set(pipe.Diameter);       // устанавливаем ширину отверстия в соответствии с размером трубы
                        height.Set(pipe.Diameter);      // устанавливаем высоту отверстия в соответствии с размером трубы
                    }
                }
            }
            transaction.Commit();

            return Result.Succeeded;
        }
    }
    public class ReferenceWithContextElementEqualityComparer : IEqualityComparer<ReferenceWithContext>
    {
        public bool Equals(ReferenceWithContext x, ReferenceWithContext y) // проверяет, будет ли 2 объекта идентичны
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(null, x)) return false;
            if (ReferenceEquals(null, y)) return false;

            var xReference = x.GetReference();
            var yReference = y.GetReference();

            return xReference.LinkedElementId == yReference.LinkedElementId
                       && xReference.ElementId == yReference.ElementId;  // являются ли точки одного и того же объекта (ID)
        }

        public int GetHashCode(ReferenceWithContext obj)  // возвращает HashCode объекта
        {
            var reference = obj.GetReference();
            unchecked
            {
                return (reference.LinkedElementId.GetHashCode() * 397) ^ reference.ElementId.GetHashCode();
            }
        }
    }
}
