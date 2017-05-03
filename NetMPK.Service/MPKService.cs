using System;
using System.Collections.Generic;
using System.Linq;
using NetMPK.Service.MPKDBTableAdapters;
using System.Windows;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace NetMPK.Service
{
    public class MPKService : IMPKService
    {
        #region Stops
        public List<string> GetStopsNames()
        { 
            using (var adapter = new StopsTableAdapter())
            {
                return adapter.GetDataOrderedByName().Select(s => s.Stop_Name).ToList();
            }
        }

        public Dictionary<string, string> GetStopsWithStreets()
        {
            using (var adapter = new Stops_StreetsTableAdapter())
            {
                return adapter.GetDataOrdered().ToDictionary(k => k.Stop_Name, v => v.Street_Name);
            }
        }

        public Tuple<int, string, string, double, double, List<int>> GetStopByName(string stopName)
        {
            Stops_StreetsTableAdapter stAdapter = new Stops_StreetsTableAdapter();
            Desc_Route_PointsTableAdapter drpAdapter = new Desc_Route_PointsTableAdapter();
            try
            {
                var stop = stAdapter.GetStopByName(stopName).Single();
                var lines = drpAdapter.GetDataByStop(stopName).Select(s => s.Line_No).Distinct().ToList();
                return Tuple.Create(stop.ID, stop.Stop_Name, stop.Street_Name, stop.X_Coord, stop.Y_Coord, lines);
            }
            finally
            {
                if (stAdapter != null)
                    stAdapter.Dispose();
                if (drpAdapter != null)
                    drpAdapter.Dispose();
            }
        }

        public List<Tuple<string, double, double>> GetStopWithCords()
        {
            using (var adapter = new StopsTableAdapter())
            {
                return adapter.GetData().Select(s => Tuple.Create(s.Stop_Name, s.X_Coord, s.Y_Coord)).ToList();
            }
        }

        public Tuple<string, double, double> GetNearestStop(double latitude, double longitude)
        {
            using (var adapter = new StopsTableAdapter())
            {
                MPKDB.StopsRow resultRow = null;
                double currentRadius = 0.0050;
                bool decreased = false;
                bool calculateCurrent = false;
                while (resultRow == null)
                {
                    var resultData = adapter.GetNearestStop(longitude, currentRadius, latitude);
                    if (resultData.Count == 0)
                    {
                        currentRadius += 0.0010;
                        if (decreased)
                        {
                            calculateCurrent = true;
                            break;
                        }
                    }
                    else
                    {
                        if (resultData.Count == 1)
                        {
                            resultRow = resultData.First();
                        }
                        else
                        {
                            currentRadius -= 0.0010;
                            decreased = true;
                        }
                    }
                }
                if (calculateCurrent)
                {
                    var tempResult = adapter.GetNearestStop(longitude, currentRadius, latitude).ToList();
                    List<Tuple<string,double>> distList = new List<Tuple<string, double>>();
                    foreach (var row in tempResult)
                    {
                        double distance = Math.Sqrt(Math.Pow(Math.Abs(longitude-row.X_Coord),2) + Math.Pow(Math.Abs(latitude - row.Y_Coord), 2));
                        distList.Add(Tuple.Create(row.Stop_Name, distance));
                    }
                    distList.OrderBy(o => o.Item2);
                    return tempResult.Where(w => w.Stop_Name == distList.First().Item1).Select(s => Tuple.Create(s.Stop_Name, s.Y_Coord, s.X_Coord)).First();
                }
                else
                {
                    return Tuple.Create(resultRow.Stop_Name, resultRow.Y_Coord, resultRow.X_Coord);
                }
            }
            return null;
        }
        #endregion

        #region Lines
        public List<Tuple<int, string, string, string, string>> GetAllLines()
        {
            using (var adapter = new LinesTableAdapter())
            {
                return adapter.GetData().Select(s => Tuple.Create(s.Line_No, s.Vehicle, s.Area, s.Daytime, s.Type)).ToList();
            }
        }
        
        public Dictionary<string, List<string>> GetLineRoutes(int lineNo)
        {
            using (var adapter = new Desc_Route_PointsTableAdapter())
            {
                Dictionary<string, List<string>> output = new Dictionary<string, List<string>>();
                var lineInfo = adapter.GetDataByLine(lineNo).ToList();
                var directions = lineInfo.Select(s => s.Direction).Distinct().ToList();
                foreach (string dir in directions)
                {
                    output.Add(dir, lineInfo.Where(w => w.Direction.Equals(dir))
                                           .OrderBy(o => o.Stop_No)
                                           .Select(s => s.Stop_Name)
                                           .ToList());
                }
                return output;
            }
        }
        
        public List<string> GetDirectionsForLine(int lineNo, string stopName)
        {
            using (var adapter = new Desc_Route_PointsTableAdapter())
            {
                return adapter.GetDataByLine(lineNo).Select(s => s.Direction).Distinct().ToList();
            }
        }

        #endregion

        #region Streets
        public string GetStreetNameByStop(string stopName)
        {
            using (var adapter = new Stops_StreetsTableAdapter())
            {
                return adapter.GetStopByName(stopName).Single().Street_Name;
            }
        }
        #endregion

        #region Timetables
        public List<List<string>> GetTimeTable(int lineNo, string stopName, string direction)
        {
            List<string> weekdays = new List<string>() { "WEEKDAY", "SATURDAY", "HOLYDAY" };
            Desc_Route_PointsTableAdapter drpAdapter = new Desc_Route_PointsTableAdapter();
            Stops_With_DistanceTableAdapter swdAdapter = new Stops_With_DistanceTableAdapter();
            Desc_Line_DeparturesTableAdapter dldAdapter = new Desc_Line_DeparturesTableAdapter();
            try
            {
                List<List<string>> output = new List<List<string>>();
                Dictionary<string, List<int>> carryOver = new Dictionary<string, List<int>>() { { "WEEKDAY", new List<int>() }, { "SATURDAY", new List<int>() }, { "HOLYDAY", new List<int>() } };
                int? tempStopNo = drpAdapter.GetStopNo(lineNo, direction, stopName);
                int stopNo;
                if (tempStopNo != null)
                { 
                    stopNo = (int)tempStopNo;
                }
                else
                {
                    direction = drpAdapter.GetOppositeDirection(lineNo,direction);
                    stopNo = (int)drpAdapter.GetStopNo(lineNo, direction, stopName);
                }
                int minutesFromStart = (int)swdAdapter.GetMinsFromStart(lineNo, direction, stopNo);
                var baseTimetables = dldAdapter.GetDataByLineAndDir(lineNo, direction).ToDictionary(k => k.Day_Type, v => v);
                for (int i = 0; i < 24; i++)
                {
                    List<string> ttRow = new List<string>();
                    foreach (var wd in weekdays)
                    {
                        List<int> cell = new List<int>();
                        List<int> stopMins = new List<int>();
                        if (baseTimetables[wd]["H" + i].ToString() != "")
                            stopMins = baseTimetables[wd]["H" + i].ToString().Split(',').Select(s => (int.Parse(s) + minutesFromStart)).ToList();
                        
                        stopMins.AddRange(new List<int>(carryOver[wd]));
                        carryOver[wd] = new List<int>();
                        foreach (int min in stopMins)
                        {
                            if (min < 60)
                                cell.Add(min);
                            else
                                carryOver[wd].Add(min - 60);
                        }
                        if (cell.Any())
                            ttRow.Add(string.Join(",", cell.OrderBy(o => o)));
                        else
                            ttRow.Add("EMPTY");
                       
                    }
                    output.Add(ttRow);
                }
                return output;
            }
            finally
            {
                if (drpAdapter != null)
                    drpAdapter.Dispose();
                if (swdAdapter != null)
                    swdAdapter.Dispose();
                if (dldAdapter != null)
                    dldAdapter.Dispose();
            }
            
        }
        #endregion

        #region RoutesSearch
        public List<List<Tuple<int, string, string, string, string, int>>> GetRoutes(string startName, string stopName)
        {
            //output:List(all routes)->List(single route)->tuple(part of route)[lineNo,firstStop,departureTime,lastStop,arrivalTime,delay]
            List<List<Tuple<int, string, string, string, string, int>>> output = new List<List<Tuple<int, string, string, string, string, int>>>();
            var routeLinesList = FindRouteLines(startName, stopName);
            foreach (var routeLines in routeLinesList)
            {
                List<Tuple<int, string, string, string, string, int>> singleRoute = new List<Tuple<int, string, string, string, string, int>>();
                var routeStops = GetIntersectStops(routeLines, startName, stopName);
                bool isValid = true;
                if (routeLines.Count == routeStops.Count)
                {
                    for (int i = 0; i < routeStops.Count; i++)
                    {
                        Tuple<string, string, int> partTimes;
                        if (singleRoute.Count==0)
                            partTimes = GetTimeForPart(routeLines[i], routeStops[i],DateTime.Now.Hour+":"+DateTime.Now.Minute);
                        else
                            partTimes = GetTimeForPart(routeLines[i], routeStops[i],singleRoute.Last().Item5);
                        if (partTimes != null)
                        { 
                            singleRoute.Add(Tuple.Create(routeLines[i], routeStops[i].Item1, FormatHour(partTimes.Item1), routeStops[i].Item2, FormatHour(partTimes.Item2), partTimes.Item3));
                        }
                        else
                        {
                            isValid = false; 
                            break;
                        }
                    }
                }
                if(isValid)
                    output.Add(singleRoute);
            }
            return output;
        }

        private string FormatHour(string hour)
        {
            var temp = hour.Split(':').ToList();
            if (temp.Count != 2)
                throw new ArgumentException();
            string outputHour = (int.Parse(temp[0]) < 10) ? "0" + temp[0] : temp[0];
            string outputMinute = (int.Parse(temp[1]) < 10) ? "0" + temp[1] : temp[1];
            return outputHour + ":" + outputMinute;
        }

        #region FindTimes
        private Tuple<string,string,int> GetTimeForPart(int lineNo, Tuple<string,string,string> routePart, string prevTime = null)
        {
            Stops_With_DistanceTableAdapter swdAdapter = new Stops_With_DistanceTableAdapter();
            Desc_Line_DeparturesTableAdapter dldAdapter = new Desc_Line_DeparturesTableAdapter();
            Desc_Route_PointsTableAdapter drpAdapter = new Desc_Route_PointsTableAdapter();
            try
            {
                _PartTime outputDepartureTime;
                _PartTime outputArrivalTime;
                int delay = 0;

                var departures = dldAdapter.GetDataByLineAndDir(lineNo, routePart.Item3)
                                           .Where(w => w.Day_Type.Equals(CurrentDayType()))
                                           .Single();

                _PartTime timeToCheck = (prevTime == null) ? new _PartTime() : new _PartTime(prevTime);

                int firstStopNo = (int)drpAdapter.GetStopNo(lineNo, routePart.Item3, routePart.Item1);
                int lastStopNo = (int)drpAdapter.GetStopNo(lineNo, routePart.Item3, routePart.Item2);

                int timeToFirst = (int)swdAdapter.GetMinsFromStart(lineNo, routePart.Item3, firstStopNo);
                int timeToLast = (int)swdAdapter.GetMinsFromStart(lineNo, routePart.Item3, lastStopNo);

                timeToCheck.SubtractMinutes(timeToFirst);
                string depMinutesFull = departures["H" + timeToCheck.hour].ToString();
                while (depMinutesFull == "")
                {
                    timeToCheck.HourUp();
                    depMinutesFull = departures["H" + timeToCheck.hour].ToString();
                }
                var depMinutes = depMinutesFull.Split(',').Select(s => int.Parse(s)).Where(w => w > timeToCheck.minute).ToList();
                if (!depMinutes.Any())
                {
                    timeToCheck.HourUp();
                    outputDepartureTime = new _PartTime(timeToCheck.hour + ":" + ((string)departures["H" + timeToCheck.hour]).Split(',').First());
                }
                else
                {
                    outputDepartureTime = new _PartTime(timeToCheck.hour + ":" + depMinutes.First());
                }
                outputArrivalTime = outputDepartureTime.Copy();
                outputDepartureTime.AddMinutes(timeToFirst);
                outputArrivalTime.AddMinutes(timeToLast);
                if (prevTime != null)
                {
                    delay = outputDepartureTime.GetDifferenceInMinutes(new _PartTime(prevTime));
                }
                return Tuple.Create(outputDepartureTime.ToString(), outputArrivalTime.ToString(), delay);
            }
            catch(InvalidOperationException)
            {
                return null;
            }
            finally
            {
                if (swdAdapter != null)
                    swdAdapter.Dispose();
                if (dldAdapter != null)
                    dldAdapter.Dispose();
                if (drpAdapter != null)
                    drpAdapter.Dispose();
            }
        }

        private string CurrentDayType()
        {
            switch (DateTime.Now.DayOfWeek)
            {
                case DayOfWeek.Sunday:
                    return "HOLYDAY";
                case DayOfWeek.Saturday:
                    return "SATURDAY";
                default:
                    return "WEEKDAY";
            }
        }

        private class _PartTime
        {
            public int hour { get; private set; }
            public int minute { get; private set; }
            public _PartTime()
            {
                hour = DateTime.Now.Hour;
                minute = DateTime.Now.Minute;
            }
            public _PartTime(string time)
            {
                var parts = time.Split(':');
                if (parts.Length == 2)
                {
                    int tempMin = -1;
                    int tempHour = -1;
                    if (int.TryParse(parts[0], out tempHour) && tempHour < 24)
                        hour = tempHour;
                    else
                        throw new ArgumentException();
                    if (int.TryParse(parts[1], out tempMin) && tempMin < 60)
                        minute = tempMin;
                    else
                        throw new ArgumentException();
                }
                else
                {
                    throw new ArgumentException();
                }
            }
            public void AddMinutes(int minutes)
            {
                int minsToAdd = minutes % 60;
                int hoursToAdd = minutes / 60;

                minute += minsToAdd;
                if (minute >= 60)
                {
                    minute -= 60;
                    hoursToAdd += 1;
                }
                hour += hoursToAdd;
                if (hour >= 24)
                    hour -= 24;

            }
            public void SubtractMinutes(int minutes)
            {
                int minsToSub = minutes % 60;
                int hoursToSub = minutes / 60;

                minute -= minsToSub;
                if (minute < 0)
                {
                    minute += 60;
                    hoursToSub += 1;
                }
                hour -= hoursToSub;
                if (hour < 0)
                    hour += 24;
            }
            public void HourUp()
            {
                hour += 1;
                if (hour >= 24)
                    hour -= 24;
            }
            public _PartTime Copy()
            {
                return new _PartTime() { hour = hour, minute = minute };
            }
            public new string ToString()
            {
                return hour + ":" + minute;
            }
            public int GetDifferenceInMinutes(_PartTime other)
            {
                return ((hour*60)+minute)- ((other.hour * 60) + other.minute);
            }
        }

        #endregion

        #region FindStops
        private List<Tuple<string, string, string>> GetIntersectStops(List<int> lines, string startName, string stopName)
        {
            using (var adapter = new GetIntersectStopsTableAdapter())
            {
                //start,stop,direction
                List<Tuple<string, string, string>> output = new List<Tuple<string, string, string>>();
                if (lines.Count == 1)
                {
                    output.Add(Tuple.Create(startName, stopName, GetDirection(lines.First(), startName, stopName)));
                }
                else
                {
                    string partStart = startName;
                    string partEnd = "";
                    for (int i = 1; i <= lines.Count - 1; i++)
                    {
                        partEnd = adapter.GetData(lines[i - 1], lines[i]).Select(s => s.Stop_Name).First();
                        output.Add(Tuple.Create(partStart, partEnd, GetDirection(lines[i - 1], partStart, partEnd)));
                        partStart = partEnd;
                    }
                    output.Add(Tuple.Create(partStart, stopName, GetDirection(lines.Last(), partStart, stopName)));
                }
                return output;
            }
        }

        private string GetDirection(int lineNo, string startName, string stopName)
        {
            using (var adapter = new Desc_Route_PointsTableAdapter())
            {
                var dataForDir = adapter.GetDataForDirection(lineNo, startName, stopName).ToList();
                if (dataForDir[0].Stop_Name.Equals(startName))
                    return dataForDir[0].Direction;
                else
                    return dataForDir[2].Direction;
            }
        }

        #endregion

        #region FindLines
        private List<List<int>> FindRouteLines(string startName, string stopName)
        {
            Desc_Route_PointsTableAdapter drpAdapter = new Desc_Route_PointsTableAdapter();
            GetIntersectLinesTableAdapter gilAdapter = new GetIntersectLinesTableAdapter();
            LinesTableAdapter lAdapter = new LinesTableAdapter();
            try
            {
                List<List<int>> output = new List<List<int>>();
                List<int> nightLines = lAdapter.GetNightLines().Select(s => s.Line_No).ToList();
                _LineInfoEqualityComparer lComparer = new _LineInfoEqualityComparer();
                List<_LineInfo> destinationLines = new List<_LineInfo>();
                drpAdapter.GetDataByStop(stopName)
                          .Select(s => s.Line_No)
                          .Distinct()
                          .ToList()
                          .ForEach(f => destinationLines.Add(new _LineInfo() {
                                                             lineNo = f,
                                                             parentLine = null }
                          ));
                List<_LineInfo> currentLines = new List<_LineInfo>();
                drpAdapter.GetDataByStop(startName)
                          .Select(s => s.Line_No)
                          .Distinct()
                          .ToList()
                          .ForEach(f => currentLines.Add(new _LineInfo()
                          {
                              lineNo = f,
                              parentLine = null
                          }
                          ));
                List<_LineInfo> intersectLines = destinationLines.Intersect(currentLines, lComparer).ToList();

                while (!intersectLines.Any())
                {
                    List<_LineInfo> linesToAdd = new List<_LineInfo>();
                    foreach (var singleLine in currentLines)
                    {
                        var singleLineIntersects = gilAdapter.GetData(singleLine.lineNo).Select(s => s.Line_No).ToList();
                        foreach (int lineNo in singleLineIntersects)
                        {
                            _LineInfo newLineInfo = new _LineInfo() { lineNo = lineNo, parentLine = (_LineInfo)singleLine.Clone() };
                            if (!linesToAdd.Contains(newLineInfo))
                                linesToAdd.Add((_LineInfo)newLineInfo.Clone());
                        }
                    }
                    foreach (var line in linesToAdd)
                    {
                        if (!currentLines.Contains(line, lComparer))
                            currentLines.Add((_LineInfo)line.Clone());
                    }
                    foreach (var cLine in currentLines)
                    {
                        if (destinationLines.Contains(cLine, lComparer))
                            intersectLines.Add((_LineInfo)cLine.Clone());
                    }
                }

                foreach (var iLine in intersectLines)
                {
                    List<int> lineOutput = new List<int>();
                    _LineInfo currentInfo = iLine;
                    while (currentInfo != null)
                    {
                        lineOutput.Add(currentInfo.lineNo);
                        currentInfo = currentInfo.parentLine;
                    }
                    output.Add(lineOutput.Reverse<int>().ToList());
                }
                if(DateTime.Now.Hour > 4 && DateTime.Now.Hour < 24)
                {
                    List<int> idxToRemove = new List<int>();
                    for(int i=0;i<output.Count;i++)
                    {
                        if (output[i].Intersect(nightLines).Any())
                            idxToRemove.Add(i);
                    }
                    foreach (int rm in idxToRemove)
                        output.RemoveAt(rm);
                }
                return output;
            }
            finally
            {
                if (drpAdapter != null)
                    drpAdapter.Dispose();
                if (gilAdapter != null)
                    gilAdapter.Dispose();
            }
        }

        private class _LineInfo : IComparable<_LineInfo>, ICloneable
        {
            public int lineNo { get; set; }
            public _LineInfo parentLine { get; set; }

            public object Clone()
            {
                return new _LineInfo { lineNo = this.lineNo, parentLine = (this.parentLine != null) ? (_LineInfo)this.parentLine.Clone() : null };
            }
            public int CompareTo(int other)
            {
                return lineNo.CompareTo(other);
            }
            public int CompareTo(_LineInfo other)
            {
                return lineNo.CompareTo(other.lineNo);
            }
        }

        private class _LineInfoEqualityComparer : IEqualityComparer<_LineInfo>
        {
            public bool Equals(_LineInfo x, int y)
            {
                return x.lineNo == y;
            }
            public bool Equals(_LineInfo x, _LineInfo y)
            {
                return x.lineNo == y.lineNo;
            }
            public int GetHashCode(_LineInfo obj)
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + obj.lineNo.GetHashCode();
                    return hash;
                }
            }
        }
        #endregion

        #endregion

        #region savedRoutes
        public bool SaveRouteForUser(string userId, string firstStopId, string lastStopId)
        {
            StopsTableAdapter sAdapter = new StopsTableAdapter();
            MPK_User_RoutesTableAdapter murAdapter = new MPK_User_RoutesTableAdapter();
            try
            {
                int parsedUserID;
                if (int.TryParse(userId, out parsedUserID))
                {
                    int _firstStopID = (int)sAdapter.GetStopIdByName(firstStopId);
                    int _lastStopID = (int)sAdapter.GetStopIdByName(lastStopId);
                    if(murAdapter.CheckRouteExists(parsedUserID,_firstStopID,_lastStopID) == 0)
                        murAdapter.Insert(parsedUserID, _firstStopID, _lastStopID);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (sAdapter != null)
                    sAdapter.Dispose();
                if (murAdapter != null)
                    murAdapter.Dispose();
            }
        }

        public List<Tuple<string, string>> GetSavedRoutesForUser(int userId)
        {
            using (var adapter = new Desc_MPK_User_RoutesTableAdapter())
            {
                return adapter.GetDataByUserID(userId).Select(s => Tuple.Create(s.First_Stop_Name, s.Last_Stop_Name)).ToList();
            }
        }
        #endregion

        #region MapDrawing

        #region GetRoutes

        public List<string> GetPointNeighbours(string stopName)
        {
            using (var adapter = new Desc_Viable_Next_StopsTableAdapter())
            {
                return adapter.GetDataByStop(stopName).Select(s => s.Next_Stop).ToList();
            }
        }

        #endregion

        #region GetPoints
        public Tuple<Dictionary<string, Vector>,Dictionary<string,List<string>>,List<Tuple<Vector,Vector>>> GetMapPoints()
        {
            StopsTableAdapter sAdapter = new StopsTableAdapter();
            Viable_LinesTableAdapter vlAdapter = new Viable_LinesTableAdapter();
            Desc_Route_PointsTableAdapter drpAdapter = new Desc_Route_PointsTableAdapter();
            Desc_Geo_Route_PointsTableAdapter dgrpAdapter = new Desc_Geo_Route_PointsTableAdapter();
            Desc_Viable_Next_StopsTableAdapter dvnsAdapter = new Desc_Viable_Next_StopsTableAdapter();
            try
            {
                int dispWidth = 1080;
                int dispHeight = 620;
                MapPointEqualityComparer mpec = new MapPointEqualityComparer();
                List<int> linesToSet = vlAdapter.GetCityLines().OrderBy(o => o.Line_No).Select(s => s.Line_No).ToList();
                var viableStops = sAdapter.GetViableStopsByArea("CITY");
                double maxX = viableStops.Select(s => s.X_Coord).Max();
                double maxY = viableStops.Select(s => s.Y_Coord).Max();
                double minX = viableStops.Select(s => s.X_Coord).Min();
                double minY = viableStops.Select(s => s.Y_Coord).Min();
                MapPointCalculator calc = new MapPointCalculator(maxX, maxY, minX, minY, dispWidth, dispHeight);
                List<MapPoint> innetOutput = new List<MapPoint>();

                foreach (int line in linesToSet)
                {
                    var lineStops = dgrpAdapter.GetRouteForLine(line).Select(s => new MapPoint() { name = s.Stop_Name, X = s.X_Coord, Y = s.Y_Coord, isValid = (s.X_Coord != -1) ? true : false }).ToList();
                    if (lineStops.Sum(s => s.isValid.GetHashCode()) != lineStops.Count)
                    {
                        var parts = GetLineParts(lineStops);
                        if (lineStops.Count != parts.Count + 1)
                            FillMissingLineParts(ref parts, ref calc);
                        foreach (var p in parts)
                        {
                            foreach (var pt in p)
                            {
                                if (!innetOutput.Contains(pt, mpec))
                                    innetOutput.Add(pt.Clone());
                            }
                        }
                    }
                    else
                    {
                        foreach (var p in lineStops)
                            if (!innetOutput.Contains(p, mpec))
                                innetOutput.Add(p);
                    }
                }
                var pointOutput = innetOutput.ToDictionary(k => k.name, v => calc.CalcPoint(v.X, v.Y));
                Dictionary<string, List<string>> closeNeighboursOutput = new Dictionary<string, List<string>>();
                var keysToCheck = pointOutput.Keys.ToList();
                int range = 15;
                while (keysToCheck.Any())
                {
                    var cp = pointOutput[keysToCheck.First()];
                    var pointsToHide = pointOutput.Where(w => w.Value.X > cp.X - range && w.Value.X < cp.X + range && w.Value.Y > cp.Y - range && w.Value.Y < cp.Y + range)
                                                  .Select(s => s.Key)
                                                  .Where(w2 => !closeNeighboursOutput.Keys.Contains(w2) && w2 != keysToCheck.First())
                                                  .ToList();
                    closeNeighboursOutput.Add(keysToCheck.First(), new List<string>(pointsToHide));
                    pointsToHide.ForEach(fe => keysToCheck.Remove(fe));
                    pointsToHide.ForEach(fe => pointOutput.Remove(fe));
                    if (keysToCheck.Count > 0)
                        keysToCheck.RemoveAt(0);
                }
                //===================================Routes=====================================================
                List<Tuple<Vector, Vector>> rtOutput;
                string path = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory) + @"\localRoutes.bin";
                if (File.Exists(path) && File.GetLastAccessTime(path).AddHours(1) > DateTime.Now)
                {
                    using (Stream str = File.Open(path, FileMode.Open))
                    {
                        BinaryFormatter bin = new BinaryFormatter();
                        rtOutput = (List<Tuple<Vector, Vector>>)bin.Deserialize(str);
                    }       
                }
                else
                {
                    var rpec = new _routePartEqualityComparer();
                    var nghRoutes = new List<_routePart>();
                    foreach (var pt in pointOutput)
                    {
                        var ngh = dvnsAdapter.GetDataByStop(pt.Key);
                        foreach (var n in ngh)
                        {
                            if (pointOutput.ContainsKey(n.Next_Stop))
                            {
                                var temp = new _routePart(new Vector(pt.Value.X, pt.Value.Y), pointOutput[n.Next_Stop]);
                                if (!nghRoutes.Contains(temp, rpec))
                                    nghRoutes.Add(temp.Clone());
                            }
                        }

                        if (closeNeighboursOutput.ContainsKey(pt.Key))
                        {
                            foreach (var clumpedStop in closeNeighboursOutput[pt.Key])
                            {
                                ngh = dvnsAdapter.GetDataByStop(clumpedStop);
                                foreach (var n in ngh)
                                {
                                    if (pointOutput.ContainsKey(n.Next_Stop))
                                    {
                                        var temp = new _routePart(new Vector(pt.Value.X, pt.Value.Y), pointOutput[n.Next_Stop]);
                                        if (!nghRoutes.Contains(temp, rpec))
                                            nghRoutes.Add(temp.Clone());
                                    }
                                    else
                                    {
                                        foreach (var pair in closeNeighboursOutput)
                                        {
                                            if (pair.Value.Contains(n.Next_Stop))
                                            {
                                                var temp = new _routePart(new Vector(pt.Value.X, pt.Value.Y), pointOutput[pair.Key]);
                                                if (!nghRoutes.Contains(temp, rpec))
                                                    nghRoutes.Add(temp.Clone());
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    rtOutput = nghRoutes.Select(s => Tuple.Create(s.v1, s.v2)).ToList();
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    using (Stream str = File.Open(path, FileMode.Create))
                    {
                        BinaryFormatter bin = new BinaryFormatter();
                        bin.Serialize(str, rtOutput);
                    }
                }
                return Tuple.Create(new Dictionary<string, Vector>(pointOutput), new Dictionary<string, List<string>>(closeNeighboursOutput), new List<Tuple<Vector, Vector>>(rtOutput));
            }
            catch(IOException)
            {
                return null;
            }
            finally
            {
                if (sAdapter != null)
                    sAdapter.Dispose();
                if (vlAdapter != null)
                    vlAdapter.Dispose();
                if (drpAdapter != null)
                    drpAdapter.Dispose();
                if (dgrpAdapter != null)
                    dgrpAdapter.Dispose();
                if (dvnsAdapter != null)
                    dvnsAdapter.Dispose();
            }
        }

        private void FillMissingLineParts(ref List<List<MapPoint>> parts, ref MapPointCalculator calc)
        {
            Desc_Points_DistanceTableAdapter dpdAdapter = new Desc_Points_DistanceTableAdapter();
            StopsTableAdapter sAdapter = new StopsTableAdapter();
            try
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    List<MapPoint> part = parts[i];
                    if (part.Count > 2)
                    {
                        MapPoint lastPoint = part.First();
                        for (int j = 1; j < part.Count - 1; j++)
                        {
                            int partDistance = 0;
                            for (int g = 1; g < part.Count; g++)
                                partDistance += (int)dpdAdapter.GetDistance(part[g - 1].name, part[g].name);

                            double ratio = (double)dpdAdapter.GetDistance(part[j - 1].name, part[j].name) / (double)partDistance;
                            MapVector pointVector = new MapVector(part.Last().X - lastPoint.X , part.Last().Y - lastPoint.Y);
                            var newPos = MapVector.AddVector(new MapVector(lastPoint.X, lastPoint.Y), MapVector.ShortenVector(pointVector, ratio));
                            part[j].X = newPos.X;
                            part[j].Y = newPos.Y;
                            part[j].isValid = true;
                            //var newCords = calc.GenerateCoordsForPoint(part[j]);
                            var stopRow = sAdapter.GetDataByStopName(part[j].name).First();
                            stopRow.X_Coord = part[j].X;
                            stopRow.Y_Coord = part[j].Y;
                            sAdapter.Update(stopRow);
                            lastPoint = part[j];
                        }
                    }
                }
            }
            finally
            {
                if (dpdAdapter != null)
                    dpdAdapter.Dispose();
                if (sAdapter != null)
                    sAdapter.Dispose();
            }
        }

        private List<List<MapPoint>> GetLineParts(List<MapPoint> line)
        {
            List<List<MapPoint>> output = new List<List<MapPoint>>();
            List<MapPoint> temp = null;
            foreach (var point in line)
            {
                if (point.isValid)
                {
                    if (temp != null)
                    {
                        temp.Add(point);
                        output.Add(temp);
                    }
                    temp = new List<MapPoint>();
                }
                temp.Add(point.Clone());
            }
            return output;
        }

        private class MapPoint
        {
            public string name { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public bool isValid { get; set; }

            public override bool Equals(object obj)
            {
                if (obj == null)
                    return false;
                MapPoint p = obj as MapPoint;
                if (p == null)
                    return false;
                return name.Equals(p.name);
            }

            public bool Equals(MapPoint p)
            {
                return name.Equals(p.name);
            }

            public override int GetHashCode()
            {
                return (int)(name.GetHashCode() ^ X.GetHashCode() ^ Y.GetHashCode());
            }

            public MapPoint Clone()
            {
                return new MapPoint() { name = name, X = X, Y = Y, isValid = isValid };
            }
        }

        private class MapPointEqualityComparer : IEqualityComparer<MapPoint>
        {
            public bool Equals(MapPoint x, MapPoint y)
            {
                return x.name.Equals(y.name);
            }

            public int GetHashCode(MapPoint obj)
            {
                return obj.name.GetHashCode();
            }
        }

        private class MapPointCalculator
        {
            private double xSpan;
            private double ySpan;
            private double minXCoord;
            private double minYCoord;

            private int dispWidth;
            private int dispHeight;

            public MapPointCalculator(double maxX, double maxY, double minX, double minY, int dWidth, int dHeight)
            {
                xSpan = maxX - minX;
                ySpan = maxY - minY;
                minXCoord = minX;
                minYCoord = minY;

                dispWidth = dWidth;
                dispHeight = dHeight;
            }

            public Vector CalcPoint(double _X, double _Y)
            {
                if (_X != -1 && _Y != -1)
                {
                    int prcX = (int)Math.Floor(((_X - minXCoord) / xSpan) * dispWidth);
                    int prcY = (int)Math.Floor(((((_Y - minYCoord) / ySpan)-1)*-1) * dispHeight);
                    return new Vector(prcX, prcY);
                }
                else
                {
                    return new Vector(-99, -99);
                }
            }

        }

        private class MapVector
        {
            public double X { get; set; }
            public double Y { get; set; }

            public MapVector(double x, double y)
            {
                X = x;
                Y = y;
            }

            public static MapVector AddVector(MapVector v1, MapVector v2)
            {
                return new MapVector(v1.X + v2.X, v1.Y + v2.Y);
            }

            public static MapVector ShortenVector(MapVector v, double s)
            {
                if (v.X != 0 || v.Y != 0)
                {
                    bool xNeg = (v.X > 0) ? false : true;
                    bool yNeg = (v.Y > 0) ? false : true;
                    double vLength = Math.Sqrt(Math.Pow(v.X, 2) + Math.Pow(v.Y, 2));
                    double sinA = Math.Abs(v.Y) / vLength;
                    double newY = sinA * (vLength * s);
                    double newX = Math.Sqrt(Math.Pow((vLength * s), 2) - Math.Pow(newY, 2));

                    if (xNeg)
                        newX *= -1;
                    if (yNeg)
                        newY *= -1;

                    return new MapVector(newX, newY);
                }
                else
                {
                    return v;
                }
            }
        }

        private class _routePart
        {
            public Vector v1 { get; set; }
            public Vector v2 { get; set; }
            public _routePart(Vector _v1, Vector _v2)
            {
                v1 = new Vector(_v1.X, _v1.Y);
                v2 = new Vector(_v2.X, _v2.Y);
            }
            public _routePart Clone()
            {
                return new _routePart(v1, v2);
            }

        }
        private class _routePartEqualityComparer : IEqualityComparer<_routePart>
        {
            public bool Equals(_routePart x, _routePart y)
            {
                if ((x.v1.X == y.v1.X && x.v1.Y == y.v1.Y) && (x.v2.X == y.v2.X && x.v2.Y == y.v2.Y))
                    return true;
                else if ((x.v1.X == y.v2.X && x.v1.Y == y.v2.Y) && (x.v2.X == y.v1.X && x.v2.Y == y.v1.Y))
                    return true;
                else
                    return false;
            }

            public int GetHashCode(_routePart obj)
            {
                return obj.v1.GetHashCode() ^ obj.v2.GetHashCode();
            }
        }

        #endregion

        #endregion

        #region AccountFunctions
        public bool LoginFree(string login)
        {
            using (var adapter = new MPK_UserTableAdapter())
            {
                var allLogins = adapter.GetData()
                                       .Select(s => s.Login)
                                       .ToList();
                return !allLogins.Contains(login);
            }
        }

        public bool EmailFree(string email)
        {
            using (var adapter = new MPK_UserTableAdapter())
            {
                var allEmails = adapter.GetData()
                                       .Select(s => s.Email)
                                       .ToList();
                return !allEmails.Contains(email);
            }
        }

        public bool RegisterUser(string login, string password, string email)
        {
            using (var adapter = new MPK_UserTableAdapter())
            {
                try
                {
                    var encryptedPassword = Encryptor.Encrypt(password);
                    adapter.Insert(login, login, encryptedPassword, email, null, 0,0);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public Tuple<bool, string> LoginUser(string login, string password)
        {
            using (var adapter = new MPK_UserTableAdapter())
            {
                var encryptedPassword = Encryptor.Encrypt(password);
                var userPass = adapter.GetUserPassword(login);
                if (userPass == null || !userPass.Equals(encryptedPassword))
                {
                    return Tuple.Create<bool,string>(false, null);
                }
                else
                {
                    return Tuple.Create(true, adapter.GetUserId(login, encryptedPassword).ToString());
                }
            }
        }

        public Tuple<bool, string> LoginEncryptedUser(string login, string encryptedPassword)
        {
            using (var adapter = new MPK_UserTableAdapter())
            {
                var userPass = adapter.GetUserPassword(login);
                if (userPass == null || !userPass.Equals(encryptedPassword))
                {
                    return Tuple.Create<bool, string>(false, null);
                }
                else
                {
                    return Tuple.Create(true, adapter.GetUserId(login, encryptedPassword).ToString());
                }
            }
        }

        public string GetEncryptedPassword(string login, string password)
        {
            using (var adapter = new MPK_UserTableAdapter())
            {
                var encryptedPassword = Encryptor.Encrypt(password);
                var userPass = adapter.GetUserPassword(login);
                if (userPass == null || !userPass.Equals(encryptedPassword))
                {
                    return null;
                }
                else
                {
                    return encryptedPassword;
                }
            }
        }
        #endregion

    }
}
