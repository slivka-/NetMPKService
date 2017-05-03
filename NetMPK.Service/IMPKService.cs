using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Web.Services.Protocols;
using System.Windows;

namespace NetMPK.Service
{
    [ServiceContract]
    public interface IMPKService
    {
        #region Stops
        [SoapHeader("Authentication")]   
        [OperationContract]
        List<string> GetStopsNames();

        [OperationContract]
        Dictionary<string, string> GetStopsWithStreets();

        [OperationContract]
        Tuple<int, string, string, double, double, List<int>> GetStopByName(string stopName);

        [OperationContract]
        List<Tuple<string, double, double>> GetStopWithCords();
        #endregion
   
        #region Lines
        [OperationContract]
        List<Tuple<int, string, string, string, string>> GetAllLines();

        [OperationContract]
        Dictionary<string, List<string>> GetLineRoutes(int lineNo);

        [OperationContract]
        List<string> GetDirectionsForLine(int lineNo, string stopName);

        [OperationContract]
        Tuple<string, double, double> GetNearestStop(double latitude, double longitude);
        #endregion

        #region Streets
        [OperationContract]
        string GetStreetNameByStop(string stopName);
        #endregion
        
        #region Timetables
        [OperationContract]
        List<List<string>> GetTimeTable(int lineNo, string stopName, string direction);
        #endregion

        #region Routes

        [OperationContract]
        List<List<Tuple<int, string, string, string, string, int>>> GetRoutes(string startName, string stopName);

        [OperationContract]
        bool SaveRouteForUser(string userId, string firstStop, string lastStop);

        [OperationContract]
        List<Tuple<string, string>> GetSavedRoutesForUser(int userId);

        #endregion

        #region MapDrawing

        [OperationContract]
        Tuple<Dictionary<string, Vector>, Dictionary<string, List<string>>, List<Tuple<Vector, Vector>>> GetMapPoints();

        [OperationContract]
        List<string> GetPointNeighbours(string stopName);

        #endregion

        #region AccountFunctions

        [OperationContract]
        bool LoginFree(string login);

        [OperationContract]
        bool EmailFree(string email);

        [OperationContract]
        bool RegisterUser(string login, string password, string email);

        [OperationContract]
        Tuple<bool,string> LoginUser(string login, string password);

        [OperationContract]
        Tuple<bool, string> LoginEncryptedUser(string login, string encryptedPassword);

        [OperationContract]
        string GetEncryptedPassword(string login, string password);
        #endregion

    }
}
