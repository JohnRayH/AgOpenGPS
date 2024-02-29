﻿using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;

namespace AgOpenGPS
{
    public class CYouTurn
    {
        #region Fields
        //copy of the mainform address
        private readonly FormGPS mf;

        /// <summary>/// triggered right after youTurnTriggerPoint is set /// </summary>
        public bool isYouTurnTriggered;

        /// <summary>  /// turning right or left?/// </summary>
        public bool isYouTurnRight;
        private int semiCircleIndex = -1;

        /// <summary> /// Is the youturn button enabled? /// </summary>
        public bool isYouTurnBtnOn;

        public double boundaryAngleOffPerpendicular, youTurnRadius;

        public int rowSkipsWidth = 1, uTurnSmoothing;

        public bool alternateSkips = false, previousBigSkip = true;
        public int rowSkipsWidth2 = 3, turnSkips = 2;

        /// <summary>  /// distance from headland as offset where to start turn shape /// </summary>
        public int youTurnStartOffset;

        //guidance values
        public double distanceFromCurrentLine, uturnDistanceFromBoundary, dxAB, dyAB;

        public double distanceFromCurrentLineSteer, distanceFromCurrentLinePivot;
        public double steerAngleGu, rEastSteer, rNorthSteer, rEastPivot, rNorthPivot;
        public double pivotCurvatureOffset, lastCurveDistance = 10000;

        private int A, B;
        private bool isHeadingSameWay = true;
        public bool isTurnCreationTooClose = false, isTurnCreationNotCrossingError = false, turnTooCloseTrigger = false;

        //pure pursuit values
        public vec3 pivot = new vec3(0, 0, 0);

        public vec2 goalPointYT = new vec2(0, 0);
        public vec2 radiusPointYT = new vec2(0, 0);
        public double steerAngleYT, rEastYT, rNorthYT, ppRadiusYT;

        //list of points for scaled and rotated YouTurn line, used for pattern, dubins, abcurve, abline
        public List<vec3> ytList = new List<vec3>();
        private List<vec3> ytList2 = new List<vec3>();

        //the list of points of next curve to build out turn and point over
        public List<vec3> outList = new List<vec3>();
        public vec3 nextLookPos = new vec3(0, 0, 0);

        //for 3Pt turns - second turn
        public List<vec3> pt3ListSecondLine = new List<vec3>();

        public int uTurnStyle = 0;

        public int pt3Phase = 0;
        public vec3 pt3TurnNewAB = new vec3(0, 0, 0);
        public bool isLastFrameForward = true;

        //is UTurn pattern in or out of bounds
        public bool isOutOfBounds = false;

        //sequence of operations of finding the next turn 0 to 3
        public int youTurnPhase;

        public double crossingheading = 0;

        // Returns 1 if the lines intersect, otherwis
        public double iE = 0, iN = 0;
        public int onA;

        // the list of possible bounds points
        public List<CClose> turnClosestList = new List<CClose>();

        //point at the farthest turn segment from pivotAxle
        public CClose closestTurnPt = new CClose();

        //where the in and out tangents cross for ALbin curve
        public CClose inClosestTurnPt = new CClose();
        public CClose outClosestTurnPt = new CClose();
        #endregion

        //constructor
        public CYouTurn(FormGPS _f)
        {
            mf = _f;

            uturnDistanceFromBoundary = Properties.Settings.Default.set_youTurnDistanceFromBoundary;

            //how far before or after boundary line should turn happen
            youTurnStartOffset = Properties.Settings.Default.set_youTurnExtensionLength;

            rowSkipsWidth = Properties.Settings.Default.set_youSkipWidth;
            Set_Alternate_skips();

            ytList.Capacity = 128;

            youTurnRadius = Properties.Settings.Default.set_youTurnRadius;

            uTurnStyle = Properties.Settings.Default.set_uTurnStyle;

            uTurnSmoothing = Properties.Settings.Default.setAS_uTurnSmoothing;
        }

        //Duh.... What does this do....
        public void DrawYouTurn()
        {
            //GL.PointSize(12.0f);
            //GL.Begin(PrimitiveType.Points);
            //GL.Color3(0.95f, 0.73f, 1.0f);
            //    GL.Vertex3(iE, iN, 0);
            //GL.End();
            //GL.PointSize(1.0f);

            int ptCount = ytList.Count;
            if (ptCount < 3) return;

            GL.PointSize(mf.ABLine.lineWidth + 2);

            if (isYouTurnTriggered)
                GL.Color3(0.95f, 0.5f, 0.95f);
            else if (isOutOfBounds || youTurnPhase != 10)
                GL.Color3(0.9495f, 0.395f, 0.325f);
            else
                GL.Color3(0.395f, 0.925f, 0.30f);

            GL.Begin(PrimitiveType.Points);
            for (int i = 0; i < ptCount; i++)
            {
                GL.Vertex3(ytList[i].easting, ytList[i].northing, 0);
            }
            GL.End();

            //if (ytList2.Count > 0)
            //{
            //    GL.Begin(PrimitiveType.Points);
            //    GL.Color3(0.95f, 0.41f, 0.980f);
            //    for (int i = 0; i < ytList2.Count; i++)
            //    {
            //        GL.Vertex3(ytList2[i].easting, ytList2[i].northing, 0);
            //    }
            //    GL.End();
            //}

            //if (outList.Count > 0)
            //{
            //    GL.PointSize(mf.ABLine.lineWidth + 2);
            //    GL.Color3(0.3f, 0.941f, 0.980f);
            //    GL.Begin(PrimitiveType.Points);
            //    for (int i = 0; i < outList.Count; i++)
            //    {
            //        GL.Vertex3(outList[i].easting, outList[i].northing, 0);
            //    }
            //    GL.End();
            //}
        }

        //Finds the point where an AB Curve crosses the turn line

        #region curve
        public bool BuildCurveDubinsYouTurn(bool isTurnLeft, vec3 pivotPos)
        {
            //grab the vehicle widths and offsets
            double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth + (isYouTurnRight ? -mf.tool.offset * 2.0 : mf.tool.offset * 2.0);

            if (uTurnStyle == 0)
            {
                if (youTurnRadius * 2 >= turnOffset)
                {
                    //ohmega turn
                    return (CreateCurveOmegaTurn(isTurnLeft, pivotPos));
                }
                else
                {
                    //Albin turn
                    return (CreateCurveWideTurn(isTurnLeft, pivotPos));
                }
            }

            else if (uTurnStyle == 1)
            {
                return (KStyleTurnCurve(isTurnLeft));
            }

            return false;

        }

        private bool CreateCurveOmegaTurn(bool isTurnLeft, vec3 pivotPos)
        {
            //grab the vehicle widths and offsets
            double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth + (isYouTurnRight ? -mf.tool.offset * 2.0 : mf.tool.offset * 2.0);
            double pointSpacing = youTurnRadius * 0.1;
            isHeadingSameWay = mf.curve.isHeadingSameWay;

            switch (youTurnPhase)
            {
                case 0: //find the crossing points
                    if (!FindCurveTurnPoints(ref mf.curve.curList))
                    {
                        FailCreate();
                        return false;
                    }

                    //save a copy 
                    inClosestTurnPt = new CClose(closestTurnPt);

                    ytList?.Clear();

                    int count = isHeadingSameWay ? -1 : 1;
                    int curveIndex = inClosestTurnPt.curveIndex + count;

                    bool pointOutOfBnd = true;
                    int stopIfWayOut = 0;

                    double head;
                    while (pointOutOfBnd)
                    {
                        stopIfWayOut++;
                        pointOutOfBnd = false;

                        //creates half a circle starting at the crossing point
                        ytList.Clear();
                        if (curveIndex >= mf.curve.curList.Count || curveIndex < 0)
                        {
                            FailCreate();
                            return false;
                        }
                        vec3 currentPos = new vec3(mf.curve.curList[curveIndex]);

                        curveIndex += count;

                        if (!isHeadingSameWay) currentPos.heading += Math.PI;
                        if (currentPos.heading >= glm.twoPI) currentPos.heading -= glm.twoPI;

                        CDubins dubYouTurnPath = new CDubins();
                        CDubins.turningRadius = youTurnRadius;

                        //now we go the other way to turn round
                        head = currentPos.heading - Math.PI;
                        if (head <= -Math.PI) head += glm.twoPI;
                        if (head >= Math.PI) head -= glm.twoPI;

                        vec3 goal = new vec3();

                        //neat trick to not have to add pi/2
                        if (isTurnLeft)
                        {
                            goal.easting = mf.curve.curList[curveIndex - count].easting + (Math.Cos(-head) * turnOffset);
                            goal.northing = mf.curve.curList[curveIndex - count].northing + (Math.Sin(-head) * turnOffset);
                        }
                        else
                        {
                            goal.easting = mf.curve.curList[curveIndex - count].easting - (Math.Cos(-head) * turnOffset);
                            goal.northing = mf.curve.curList[curveIndex - count].northing - (Math.Sin(-head) * turnOffset);
                        }

                        goal.heading = head;

                        //generate the turn points
                        ytList = dubYouTurnPath.GenerateDubins(currentPos, goal);
                        if (ytList.Count == 0) return false;

                        if (stopIfWayOut == 300)
                        {
                            //for some reason it doesn't go inside boundary, return empty list
                            FailCreate();
                            return false;
                        }

                        for (int i = 0; i < ytList.Count; i++)
                        {
                            if (mf.bnd.IsPointInsideTurnArea(ytList[i]) == -1)
                            {
                                pointOutOfBnd = true;
                                break;
                            }
                        }
                    }

                    //move out
                    //too many points from Dubins - so cut
                    double distance;

                    int cnt = ytList.Count;
                    for (int i = 1; i < cnt - 2; i++)
                    {
                        distance = glm.DistanceSquared(ytList[i], ytList[i + 1]);
                        if (distance < pointSpacing)
                        {
                            ytList.RemoveAt(i + 1);
                            i--;
                            cnt = ytList.Count;
                        }
                    }

                    head = ytList[0].heading;
                    double cosHead = Math.Cos(head) * 0.1;
                    double sinHead = Math.Sin(head) * 0.1;
                    vec3[] arr2 = new vec3[ytList.Count];
                    ytList.CopyTo(arr2);
                    ytList.Clear();

                    semiCircleIndex = -1;
                    pointOutOfBnd = false;

                    while (!pointOutOfBnd)
                    {
                        stopIfWayOut++;
                        pointOutOfBnd = false;
                        mf.distancePivotToTurnLine = glm.DistanceSquared(arr2[0], mf.pivotAxlePos);

                        for (int i = 0; i < arr2.Length; i++)
                        {
                            arr2[i].easting += (sinHead);
                            arr2[i].northing += (cosHead);
                        }

                        //step 2 move the turn inside with steps of 0.1 meter
                        int j;
                        for (j = 0; j < arr2.Length; j++)
                        {
                            if (mf.bnd.IsPointInsideTurnArea(arr2[j]) != 0)
                            {
                                pointOutOfBnd = true;
                                break;
                            }
                        }

                        if (stopIfWayOut == 300 || (mf.distancePivotToTurnLine < 6))
                        {
                            FailCreate();
                            return false;
                        }
                    }

                    ytList.AddRange(arr2);
                    youTurnPhase = 2;
                    break;


                case 2:
                    AddSequenceLines(ytList[0].heading - Math.PI);
                    youTurnPhase = 10;
                    break;
            }
            return true;
        }

        private bool CreateCurveWideTurn(bool isTurnLeft, vec3 pivotPos)
        {
            //grab the vehicle widths and offsets
            double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth + (isYouTurnRight ? -mf.tool.offset * 2.0 : mf.tool.offset * 2.0);
            double pointSpacing = youTurnRadius * 0.1;

            isHeadingSameWay = mf.curve.isHeadingSameWay;

            switch (youTurnPhase)
            {
                case 0: //find the crossing points
                    if (!FindCurveTurnPoints(ref mf.curve.curList))
                    {
                        FailCreate();
                        return false;
                    }

                    //save a copy 
                    inClosestTurnPt = new CClose(closestTurnPt);

                    ytList?.Clear();

                    int count = isHeadingSameWay ? -1 : 1;
                    int curveIndex = inClosestTurnPt.curveIndex + count;

                    bool pointOutOfBnd = true;
                    int stopIfWayOut = 0;

                    double head = 0;

                    while (pointOutOfBnd)
                    {
                        stopIfWayOut++;
                        pointOutOfBnd = false;

                        //creates half a circle starting at the crossing point
                        ytList.Clear();
                        if (curveIndex >= mf.curve.curList.Count || curveIndex < 0)
                        {
                            FailCreate();
                            return false;
                        }
                        vec3 currentPos = new vec3(mf.curve.curList[curveIndex]);

                        curveIndex += count;

                        if (!isHeadingSameWay) currentPos.heading += Math.PI;
                        if (currentPos.heading >= glm.twoPI) currentPos.heading -= glm.twoPI;

                        ytList.Add(currentPos);

                        while (Math.Abs(ytList[0].heading - currentPos.heading) < Math.PI)
                        {
                            //Update the position of the car
                            currentPos.easting += pointSpacing * Math.Sin(currentPos.heading);
                            currentPos.northing += pointSpacing * Math.Cos(currentPos.heading);

                            //Which way are we turning?
                            double turnParameter = isTurnLeft ? -1.0 : 1.0;

                            //Update the heading
                            currentPos.heading += (pointSpacing / youTurnRadius) * turnParameter;

                            //Add the new coordinate to the path
                            ytList.Add(currentPos);
                        }

                        for (int i = 0; i < ytList.Count; i++)
                        {
                            if (mf.bnd.IsPointInsideTurnArea(ytList[i]) == -1)
                            {
                                pointOutOfBnd = true;
                                break;
                            }
                        }
                    }

                    //move out
                    head = ytList[0].heading;
                    double cosHead = Math.Cos(head) * 0.1;
                    double sinHead = Math.Sin(head) * 0.1;
                    vec3[] arr2 = new vec3[ytList.Count];
                    ytList.CopyTo(arr2);
                    ytList.Clear();

                    semiCircleIndex = -1;
                    //step 2 move the turn inside with steps of 0.1 meter
                    int j = 0;
                    pointOutOfBnd = false;

                    while (!pointOutOfBnd)
                    {
                        stopIfWayOut++;
                        pointOutOfBnd = false;
                        mf.distancePivotToTurnLine = glm.DistanceSquared(arr2[0], mf.pivotAxlePos);

                        for (int i = 0; i < arr2.Length; i++)
                        {
                            arr2[i].easting += (sinHead);
                            arr2[i].northing += (cosHead);
                        }

                        for (j = 0; j < arr2.Length; j++)
                        {
                            if (mf.bnd.IsPointInsideTurnArea(arr2[j]) != 0)
                            {
                                pointOutOfBnd = true;
                                semiCircleIndex = j;
                                break;
                            }
                        }

                        if (stopIfWayOut == 300 || (mf.distancePivotToTurnLine < 6))
                        {
                            //for some reason it doesn't go inside boundary, return empty list
                            return false;
                        }
                    }

                    for (int a = 0; a <= semiCircleIndex; a++)
                    {
                        if (arr2[a].heading >= glm.twoPI) arr2[a].heading -= glm.twoPI;
                        if (arr2[a].heading < 0) arr2[a].heading += glm.twoPI;
                        ytList.Add(arr2[a]);
                    }

                    //add start extension from curve points
                    curveIndex -= count;

                    if (!isTurnLeft)
                    {
                        nextLookPos.easting = pivotPos.easting + (Math.Cos(-pivotPos.heading) * turnOffset);
                        nextLookPos.northing = pivotPos.northing + (Math.Sin(-pivotPos.heading) * turnOffset);
                    }
                    else
                    {
                        nextLookPos.easting = pivotPos.easting - (Math.Cos(-pivotPos.heading) * turnOffset);
                        nextLookPos.northing = pivotPos.northing - (Math.Sin(-pivotPos.heading) * turnOffset);
                    }

                    mf.curve.BuildOutGuidanceList(pivotPos);

                    for (int i = 0; i < 4; i++)
                    {
                        ytList.Insert(0, new vec3(mf.curve.curList[curveIndex + i * count]));
                    }


                    //outbound curve  -----------------------------------------------------------------------------------------

                    //if left turn, counts down - right counts up

                    //zip down the line and find closest crossing to in crossing
                    FindOutTurnPoint(ref outList, inClosestTurnPt.turnLineNum);
                    ytList2?.Clear();

                    curveIndex = closestTurnPt.curveIndex + count;

                    pointOutOfBnd = true;
                    stopIfWayOut = 0;

                    head = 0;

                    while (pointOutOfBnd)
                    {
                        stopIfWayOut++;
                        pointOutOfBnd = false;

                        //creates half a circle starting at the crossing point
                        ytList2.Clear();

                        if (curveIndex >= outList.Count || curveIndex < 0)
                        {
                            FailCreate();
                            return false;
                        }
                        vec3 currentPos = new vec3(outList[curveIndex]);

                        curveIndex += count;

                        if (!isHeadingSameWay) currentPos.heading += Math.PI;
                        if (currentPos.heading >= glm.twoPI) currentPos.heading -= glm.twoPI;

                        ytList2.Add(currentPos);

                        while (Math.Abs(ytList2[0].heading - currentPos.heading) < Math.PI)
                        {
                            //Update the position of the car
                            currentPos.easting += pointSpacing * Math.Sin(currentPos.heading);
                            currentPos.northing += pointSpacing * Math.Cos(currentPos.heading);

                            //Which way are we turning?
                            double turnParameter = isTurnLeft ? 1.0 : -1.0;

                            //Update the heading
                            currentPos.heading += (pointSpacing / youTurnRadius) * turnParameter;

                            //Add the new coordinate to the path
                            ytList2.Add(currentPos);
                        }

                        for (int i = 0; i < ytList2.Count; i++)
                        {
                            if (mf.bnd.IsPointInsideTurnArea(ytList2[i]) == -1)
                            {
                                pointOutOfBnd = true;
                                break;
                            }
                        }
                    }

                    //move out
                    head = ytList2[0].heading;
                    cosHead = Math.Cos(head) * 0.1;
                    sinHead = Math.Sin(head) * 0.1;
                    arr2 = new vec3[ytList2.Count];
                    ytList2.CopyTo(arr2);
                    ytList2.Clear();

                    semiCircleIndex = -1;
                    pointOutOfBnd = false;

                    while (!pointOutOfBnd)
                    {
                        stopIfWayOut++;
                        pointOutOfBnd = false;
                        mf.distancePivotToTurnLine = glm.DistanceSquared(arr2[0], mf.pivotAxlePos);

                        for (int i = 0; i < arr2.Length; i++)
                        {
                            arr2[i].easting += (sinHead);
                            arr2[i].northing += (cosHead);
                        }

                        for (j = 0; j < arr2.Length; j++)
                        {
                            if (mf.bnd.IsPointInsideTurnArea(arr2[j]) != 0)
                            {
                                pointOutOfBnd = true;
                                semiCircleIndex = j;
                                break;
                            }
                        }


                        if (stopIfWayOut == 300 || (mf.distancePivotToTurnLine < 6))
                        {
                            //for some reason it doesn't go inside boundary, return empty list
                            return false;
                        }
                    }

                    for (int a = 0; a <= semiCircleIndex; a++)
                    {
                        ytList2.Add(arr2[a]);
                    }

                    //add start extension from curve points
                    curveIndex -= count;

                    for (int i = 0; i < 4; i++)
                    {
                        ytList2.Insert(0, new vec3(outList[curveIndex + i * count]));
                    }

                    // 2 ARCS MADE
                    youTurnPhase = 1;

                    break;


                case 1:
                    int cnt1 = ytList.Count;
                    int cnt2 = ytList2.Count;

                    //finds out start and goal point along the tunline
                    FindInnerTurnPoints(ref ytList);
                    inClosestTurnPt = new CClose(closestTurnPt);

                    FindInnerTurnPoints(ref ytList2);
                    outClosestTurnPt = new CClose(closestTurnPt);

                    //we have 2 different turnLine crossings
                    if (inClosestTurnPt.turnLineNum != outClosestTurnPt.turnLineNum)
                    {
                        FailCreate();
                        return false;
                    }

                    //is in and out on same segment? so only 1 segment
                    if (inClosestTurnPt.turnLineIndex == outClosestTurnPt.turnLineIndex)
                    {
                        for (int a = ytList2.Count - 1; a >= 0; a--)
                        {
                            ytList.Add(new vec3(ytList2[a]));
                        }
                    }
                    else
                    {
                        //multiple segments
                        bool isTurnLineSameWay = true;
                        if (Math.Abs(inClosestTurnPt.closePt.heading - ytList[cnt1 - 1].heading) > glm.PIBy2)
                            isTurnLineSameWay = false;

                        vec3 tPoint = new vec3();
                        int turnCount = mf.bnd.bndList[inClosestTurnPt.turnLineNum].turnLine.Count;

                        //how many points from turnline do we add
                        int loops = Math.Abs(inClosestTurnPt.turnLineIndex - outClosestTurnPt.turnLineIndex);

                        //are we crossing a border?
                        if (loops > (mf.bnd.bndList[inClosestTurnPt.turnLineNum].turnLine.Count / 2))
                        {
                            if (inClosestTurnPt.turnLineIndex < outClosestTurnPt.turnLineIndex)
                            {
                                loops = (turnCount - outClosestTurnPt.turnLineIndex) + inClosestTurnPt.turnLineIndex;
                            }
                            else
                            {
                                loops = (turnCount - inClosestTurnPt.turnLineIndex) + outClosestTurnPt.turnLineIndex;
                            }
                        }

                        //count up - start with B which is next A
                        if (isTurnLineSameWay)
                        {
                            for (int i = 0; i < loops; i++)
                            {
                                if ((inClosestTurnPt.turnLineIndex + 1) >= turnCount) inClosestTurnPt.turnLineIndex = -1;

                                tPoint = mf.bnd.bndList[inClosestTurnPt.turnLineNum].turnLine[inClosestTurnPt.turnLineIndex + 1];
                                inClosestTurnPt.turnLineIndex++;
                                if (inClosestTurnPt.turnLineIndex >= turnCount)
                                    inClosestTurnPt.turnLineIndex = 0;
                                ytList.Add(tPoint);
                            }
                        }
                        else //count down = start with A
                        {
                            for (int i = 0; i < loops; i++)
                            {
                                tPoint = mf.bnd.bndList[inClosestTurnPt.turnLineNum].turnLine[inClosestTurnPt.turnLineIndex];
                                inClosestTurnPt.turnLineIndex--;
                                if (inClosestTurnPt.turnLineIndex == -1)
                                    inClosestTurnPt.turnLineIndex = turnCount - 1;
                                ytList.Add(tPoint);
                            }
                        }

                        //add the out from ytList2
                        for (int a = ytList2.Count - 1; a > -1; a--)
                        {
                            ytList.Add(new vec3(ytList2[a]));
                        }
                    }

                    ytList2?.Clear();

                    if (isTurnCreationTooClose)
                    {
                        FailCreate();
                        return false;
                    }

                    //fill in the gaps
                    double distance;

                    int cnt = ytList.Count;
                    for (int i = 1; i < cnt - 2; i++)
                    {
                        j = i + 1;
                        if (j == cnt - 1) continue;
                        distance = glm.DistanceSquared(ytList[i], ytList[j]);
                        if (distance > 1)
                        {
                            vec3 pointB = new vec3((ytList[i].easting + ytList[j].easting) / 2.0,
                                (ytList[i].northing + ytList[j].northing) / 2.0, ytList[i].heading);

                            ytList.Insert(j, pointB);
                            cnt = ytList.Count;
                            i--;
                        }
                    }

                    //calculate the new points headings based on fore and aft of point - smoother turns
                    cnt = ytList.Count;
                    vec3[] arr = new vec3[cnt];
                    cnt -= 2;
                    ytList.CopyTo(arr);
                    ytList.Clear();


                    for (int i = 0; i < cnt; i++)
                    {
                        vec3 pt3 = new vec3(arr[i]);
                        pt3.heading = Math.Atan2(arr[i + 1].easting - arr[i].easting,
                            arr[i + 1].northing - arr[i].northing);
                        if (pt3.heading < 0) pt3.heading += glm.twoPI;
                        ytList.Add(pt3);
                    }

                    isOutOfBounds = false;
                    youTurnPhase = 2;
                    turnTooCloseTrigger = false;
                    isTurnCreationTooClose = false;
                    return true;

                case 2:

                    youTurnPhase = 10;

                    break;

                default:
                    FailCreate();
                    return false;
            }

            //youTurnPhase = 3;
            return true;
        }

        public bool KStyleTurnCurve(bool isTurnLeft)
        {
            //grab the vehicle widths and offsets
            double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth + (isYouTurnRight ? -mf.tool.offset * 2.0 : mf.tool.offset * 2.0);
            double pointSpacing = youTurnRadius * 0.1;

            isHeadingSameWay = mf.curve.isHeadingSameWay;

            int turnIndex = mf.bnd.IsPointInsideTurnArea(mf.pivotAxlePos);
            if (turnIndex != 0 || mf.makeUTurnCounter < 20)
            {
                youTurnPhase = 0;
                return true;
            }

            mf.makeUTurnCounter = 0;

            if (youTurnPhase == 0)
            {
                if (!FindCurveTurnPoints(ref mf.curve.curList))
                {
                    FailCreate();
                    return false;
                }

                //save a copy 
                inClosestTurnPt = new CClose(closestTurnPt);

                ytList?.Clear();

                int count = isHeadingSameWay ? -1 : 1;
                int curveIndex = inClosestTurnPt.curveIndex + count;

                bool pointOutOfBnd = true;
                int stopIfWayOut = 0;

                double head = 0;

                while (pointOutOfBnd)
                {
                    stopIfWayOut++;
                    pointOutOfBnd = false;

                    //creates half a circle starting at the crossing point
                    ytList.Clear();
                    if (curveIndex >= mf.curve.curList.Count || curveIndex < 0)
                    {
                        FailCreate();
                        return false;
                    }
                    vec3 currentPos = new vec3(mf.curve.curList[curveIndex]);

                    curveIndex += count;

                    if (!isHeadingSameWay) currentPos.heading += Math.PI;
                    if (currentPos.heading >= glm.twoPI) currentPos.heading -= glm.twoPI;

                    ytList.Add(currentPos);

                    while (Math.Abs(ytList[0].heading - currentPos.heading) < 2.2)
                    {
                        //Update the position of the car
                        currentPos.easting += pointSpacing * Math.Sin(currentPos.heading);
                        currentPos.northing += pointSpacing * Math.Cos(currentPos.heading);

                        //Which way are we turning?
                        double turnParameter = isTurnLeft ? -1.0 : 1.0;

                        //Update the heading
                        currentPos.heading += (pointSpacing / youTurnRadius) * turnParameter;

                        //Add the new coordinate to the path
                        ytList.Add(currentPos);
                    }

                    for (int i = 0; i < ytList.Count; i++)
                    {
                        if (mf.bnd.IsPointInsideTurnArea(ytList[i]) == -1)
                        {
                            pointOutOfBnd = true;
                            break;
                        }
                    }
                }

                //move out
                head = ytList[0].heading;
                double cosHead = Math.Cos(head) * 0.1;
                double sinHead = Math.Sin(head) * 0.1;
                vec3[] arr2 = new vec3[ytList.Count];
                ytList.CopyTo(arr2);
                ytList.Clear();

                //step 2 move the turn inside with steps of 0.1 meter
                int j = 0;
                pointOutOfBnd = false;

                while (!pointOutOfBnd)
                {
                    stopIfWayOut++;
                    pointOutOfBnd = false;
                    mf.distancePivotToTurnLine = glm.DistanceSquared(arr2[0], mf.pivotAxlePos);

                    for (int i = 0; i < arr2.Length; i++)
                    {
                        arr2[i].easting += (sinHead);
                        arr2[i].northing += (cosHead);
                    }

                    for (j = 0; j < arr2.Length; j++)
                    {
                        if (mf.bnd.IsPointInsideTurnArea(arr2[j]) != 0)
                        {
                            pointOutOfBnd = true;
                            break;
                        }
                    }

                    if (stopIfWayOut == 300 || (mf.distancePivotToTurnLine < 6))
                    {
                        //for some reason it doesn't go inside boundary, return empty list
                        return false;
                    }
                }

                ytList.AddRange(arr2);

                //add start extension from curve points
                curveIndex -= count;

                //point used to set next guidance line
                pt3TurnNewAB = new vec3(ytList[0]);

                //now we go the other way to turn round
                head = ytList[0].heading;
                head -= Math.PI;
                if (head < -Math.PI) head += glm.twoPI;
                if (head > Math.PI) head -= glm.twoPI;

                if (isTurnLeft)
                {
                    pt3TurnNewAB.easting += (Math.Cos(-head) * turnOffset);
                    pt3TurnNewAB.northing += (Math.Sin(-head) * turnOffset);
                }
                else
                {
                    pt3TurnNewAB.easting -= (Math.Cos(-head) * turnOffset);
                    pt3TurnNewAB.northing -= (Math.Sin(-head) * turnOffset);
                }

                if (head >= glm.twoPI) head -= glm.twoPI;
                else if (head < 0) head += glm.twoPI;

                pt3TurnNewAB.heading = head;

                //add the tail to first turn
                head = ytList[ytList.Count - 1].heading;

                vec3 pt;
                for (int i = 1; i <= (int)(3 * turnOffset); i++)
                {
                    pt.easting = ytList[ytList.Count - 1].easting + (Math.Sin(head) * 0.5);
                    pt.northing = ytList[ytList.Count - 1].northing + (Math.Cos(head) * 0.5);
                    pt.heading = 0;
                    ytList.Add(pt);
                }

                //leading in line of turn
                for (int i = 0; i < 4; i++)
                {
                    ytList.Insert(0, new vec3(mf.curve.curList[curveIndex + i * count]));
                }

                //fill in the gaps
                double distance;

                int cnt = ytList.Count;
                for (int i = 1; i < cnt - 2; i++)
                {
                    j = i + 1;
                    if (j == cnt - 1) continue;
                    distance = glm.DistanceSquared(ytList[i], ytList[j]);
                    if (distance > 1)
                    {
                        vec3 pointB = new vec3((ytList[i].easting + ytList[j].easting) / 2.0,
                            (ytList[i].northing + ytList[j].northing) / 2.0, ytList[i].heading);

                        ytList.Insert(j, pointB);
                        cnt = ytList.Count;
                        i--;
                    }
                }

                //calculate line headings
                vec3[] arr = new vec3[ytList.Count];
                ytList.CopyTo(arr);
                ytList.Clear();

                for (int i = 0; i < arr.Length - 1; i++)
                {
                    arr[i].heading = Math.Atan2(arr[i + 1].easting - arr[i].easting, arr[i + 1].northing - arr[i].northing);
                    if (arr[i].heading < 0) arr[i].heading += glm.twoPI;
                    ytList.Add(arr[i]);
                }

                mf.distancePivotToTurnLine = glm.Distance(ytList[0], mf.pivotAxlePos);

                isOutOfBounds = false;
                youTurnPhase = 10;
                turnTooCloseTrigger = false;
                isTurnCreationTooClose = false;
                return true;
            }

            return true;
        }


        public bool FindOutTurnPoint(ref List<vec3> xList, int turnNum)
        {
            //find closet AB Curve point that will cross and go out of bounds
            turnClosestList?.Clear();

            for (int j = 0; j < xList.Count - 1; j++)
            {
                for (int i = 0; i < mf.bnd.bndList[turnNum].turnLine.Count - 1; i++)
                {
                    int res = GetLineIntersection(
                            mf.bnd.bndList[turnNum].turnLine[i].easting,
                            mf.bnd.bndList[turnNum].turnLine[i].northing,
                            mf.bnd.bndList[turnNum].turnLine[i + 1].easting,
                            mf.bnd.bndList[turnNum].turnLine[i + 1].northing,

                            xList[j].easting,
                            xList[j].northing,
                            xList[j + 1].easting,
                            xList[j + 1].northing,

                             ref iE, ref iN);

                    if (res == 1)
                    {
                        closestTurnPt = new CClose();
                        closestTurnPt.closePt.easting = iE;
                        closestTurnPt.closePt.northing = iN;
                        closestTurnPt.turnLineIndex = i;
                        closestTurnPt.curveIndex = j;
                        closestTurnPt.turnLineNum = turnNum;

                        turnClosestList.Add(closestTurnPt);

                        break;
                    }
                }
            }

            //determine closest point
            double minDistance = double.MaxValue;

            if (turnClosestList.Count > 0)
            {
                for (int i = 0; i < turnClosestList.Count; i++)
                {
                    double dist =
                        ((inClosestTurnPt.closePt.easting - turnClosestList[i].closePt.easting)
                        * (inClosestTurnPt.closePt.easting - turnClosestList[i].closePt.easting)) +

                        ((inClosestTurnPt.closePt.northing - turnClosestList[i].closePt.northing)
                        * (inClosestTurnPt.closePt.northing - turnClosestList[i].closePt.northing));

                    if (minDistance >= dist)
                    {

                        minDistance = dist;
                        closestTurnPt = new CClose(turnClosestList[i]);
                    }
                }
            }
            else
            {
                return false;
            }

            return true;
        }

        public bool FindInnerTurnPoints(ref List<vec3> xList)
        {
            //find closet AB Curve point that will cross and go out of bounds
            int turnNum = 99;
            int j;

            closestTurnPt = new CClose();

            for (j = 0; j < xList.Count; j++)
            {
                int turnIndex = mf.bnd.IsPointInsideTurnArea(xList[j]);
                if (turnIndex != 0)
                {
                    closestTurnPt.curveIndex = j - 1;
                    closestTurnPt.turnLineNum = turnIndex;
                    turnNum = turnIndex;
                    break;
                }
            }

            if (turnNum < 0) //uturn will be on outer boundary turn
            {
                closestTurnPt.turnLineNum = 0;
                turnNum = 0;
            }
            else if (turnNum == 99)
            {
                //curve does not cross a boundary - oops
                isTurnCreationNotCrossingError = true;
                return false;
            }

            if (closestTurnPt.curveIndex == -1)
            {
                isTurnCreationNotCrossingError = true;
                return false;
            }

            for (int i = 0; i < mf.bnd.bndList[turnNum].turnLine.Count - 1; i++)
            {
                int res = GetLineIntersection(
                        mf.bnd.bndList[turnNum].turnLine[i].easting,
                        mf.bnd.bndList[turnNum].turnLine[i].northing,
                        mf.bnd.bndList[turnNum].turnLine[i + 1].easting,
                        mf.bnd.bndList[turnNum].turnLine[i + 1].northing,

                        xList[closestTurnPt.curveIndex].easting,
                        xList[closestTurnPt.curveIndex].northing,
                        xList[closestTurnPt.curveIndex + 1].easting,
                        xList[closestTurnPt.curveIndex + 1].northing,

                         ref iE, ref iN);

                if (res == 1)
                {
                    closestTurnPt.closePt.easting = iE;
                    closestTurnPt.closePt.northing = iN;

                    double hed = Math.Atan2(mf.bnd.bndList[turnNum].turnLine[i + 1].easting - mf.bnd.bndList[turnNum].turnLine[i].easting,
                        mf.bnd.bndList[turnNum].turnLine[i + 1].northing - mf.bnd.bndList[turnNum].turnLine[i].northing);
                    if (hed < 0) hed += glm.twoPI;
                    crossingheading = hed;
                    closestTurnPt.closePt.heading = hed;
                    closestTurnPt.turnLineIndex = i;

                    break;
                }
            }

            return closestTurnPt.turnLineIndex != -1 && closestTurnPt.curveIndex != -1;
            //return true;
        }

        public bool FindCurveTurnPoints(ref List<vec3> xList)
        {
            //find closet AB Curve point that will cross and go out of bounds
            int Count = mf.curve.isHeadingSameWay ? 1 : -1;
            int turnNum = 99;
            int j;

            closestTurnPt = new CClose();

            for (j = mf.curve.currentLocationIndex; j > 0 && j < xList.Count; j += Count)
            {
                int turnIndex = mf.bnd.IsPointInsideTurnArea(xList[j]);
                if (turnIndex != 0)
                {
                    closestTurnPt.curveIndex = j - Count;
                    closestTurnPt.turnLineNum = turnIndex;
                    turnNum = turnIndex;
                    break;
                }
            }

            if (turnNum < 0) //uturn will be on outer boundary turn
            {
                closestTurnPt.turnLineNum = 0;
                turnNum = 0;
            }
            else if (turnNum == 99)
            {
                //curve does not cross a boundary - oops
                isTurnCreationNotCrossingError = true;
                return false;
            }

            if (closestTurnPt.curveIndex == -1)
            {
                isTurnCreationNotCrossingError = true;
                return false;
            }


            for (int i = 0; i < mf.bnd.bndList[turnNum].turnLine.Count - 1; i++)
            {
                int res = GetLineIntersection(
                        mf.bnd.bndList[turnNum].turnLine[i].easting,
                        mf.bnd.bndList[turnNum].turnLine[i].northing,
                        mf.bnd.bndList[turnNum].turnLine[i + 1].easting,
                        mf.bnd.bndList[turnNum].turnLine[i + 1].northing,

                        xList[closestTurnPt.curveIndex].easting,
                        xList[closestTurnPt.curveIndex].northing,
                        xList[closestTurnPt.curveIndex + Count].easting,
                        xList[closestTurnPt.curveIndex + Count].northing,

                         ref iE, ref iN);

                if (res == 1)
                {
                    closestTurnPt.closePt.easting = iE;
                    closestTurnPt.closePt.northing = iN;

                    double hed = Math.Atan2(mf.bnd.bndList[turnNum].turnLine[i + 1].easting - mf.bnd.bndList[turnNum].turnLine[i].easting,
                        mf.bnd.bndList[turnNum].turnLine[i + 1].northing - mf.bnd.bndList[turnNum].turnLine[i].northing);
                    if (hed < 0) hed += glm.twoPI;
                    crossingheading = hed;
                    closestTurnPt.closePt.heading = hed;
                    closestTurnPt.turnLineIndex = i;

                    break;
                }
            }

            return closestTurnPt.turnLineIndex != -1 && closestTurnPt.curveIndex != -1;
            //return true;
        }

        #endregion

        #region ABLine
        public bool BuildABLineDubinsYouTurn(bool isTurnLeft)
        {
            if (!mf.isBtnAutoSteerOn) mf.ABLine.isHeadingSameWay
                    = Math.PI - Math.Abs(Math.Abs(mf.fixHeading - mf.ABLine.abHeading) - Math.PI) < glm.PIBy2;

            double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth
                + (isYouTurnRight ? -mf.tool.offset * 2.0 : mf.tool.offset * 2.0);

            if (uTurnStyle == 0)
            {
                if (youTurnRadius * 2 >= turnOffset)
                {
                    if (CreateABLineOmegaTurn(isTurnLeft)) return true;
                }
                else
                {
                    return (CreateABLineWideTurn(isTurnLeft));
                }
            }

            else if (uTurnStyle == 1)
            {
                return (KStyleTurnAB(isTurnLeft));
            }

            return false;
        }

        private bool CreateABLineWideTurn(bool isTurnLeft)
        {
            double pointSpacing = youTurnRadius * 0.1;

            //step 1 turn in to the turnline
            if (youTurnPhase == 0)
            {
                isOutOfBounds = true;
                //timer.Start();
                //grab the pure pursuit point right on ABLine
                vec3 onPurePoint = new vec3(mf.ABLine.rEastAB, mf.ABLine.rNorthAB, 0);

                //how far are we from any turn boundary
                FindClosestTurnPoint(onPurePoint);

                //save a copy for first point
                inClosestTurnPt = new CClose(closestTurnPt);

                //already no turnline
                if (inClosestTurnPt.turnLineIndex == -1) return false;

                //calculate the distance to the turnline
                mf.distancePivotToTurnLine = glm.Distance(mf.pivotAxlePos, closestTurnPt.closePt);

                //point on AB line closest to pivot axle point from ABLine PurePursuit aka where we are
                rEastYT = mf.ABLine.rEastAB;
                rNorthYT = mf.ABLine.rNorthAB;
                isHeadingSameWay = mf.ABLine.isHeadingSameWay;
                double head = mf.ABLine.abHeading;

                if (!isHeadingSameWay) head += Math.PI;
                if (head >= glm.twoPI) head -= glm.twoPI;

                //thistance to turnline from where we are
                double turnDiagDistance = mf.distancePivotToTurnLine;

                //moves the point to the crossing with the turnline
                rEastYT += (Math.Sin(head) * turnDiagDistance);
                rNorthYT += (Math.Cos(head) * turnDiagDistance);

                //creates half a circle starting at the crossing point
                ytList.Clear();
                vec3 currentPos = new vec3(rEastYT, rNorthYT, head);
                ytList.Add(currentPos);

                ///CDubins.turningRadius = youTurnRadius;
                //Taken from Dubbins
                while (Math.Abs(head - currentPos.heading) < Math.PI)
                {
                    //Update the position of the car
                    currentPos.easting += pointSpacing * Math.Sin(currentPos.heading);
                    currentPos.northing += pointSpacing * Math.Cos(currentPos.heading);

                    //Which way are we turning?
                    double turnParameter = 1.0;

                    if (isTurnLeft) turnParameter = -1.0;

                    //Update the heading
                    currentPos.heading += (pointSpacing / youTurnRadius) * turnParameter;

                    //Add the new coordinate to the path
                    ytList.Add(currentPos);
                }

                //move the half circle to tangent the turnline
                ytList = MoveABTurnInsideTurnLine(ytList, head);

                //if it couldn't be done this will trigger
                if (ytList.Count < 5 || semiCircleIndex == -1)
                {
                    FailCreate();
                    return false;
                }

                //we need to delete the points after the point that is outside and correct the heading
                int cnt = ytList.Count;
                vec3[] arr23 = new vec3[cnt];
                ytList.CopyTo(arr23);
                ytList.Clear();

                for (int i = 0; i < cnt; i++)
                {
                    if (i == semiCircleIndex)
                        break;
                    if (arr23[i].heading >= glm.twoPI) arr23[i].heading -= glm.twoPI;
                    else if (arr23[i].heading < 0) arr23[i].heading += glm.twoPI;
                    ytList.Add(arr23[i]);
                }

                mf.distancePivotToTurnLine = glm.Distance(ytList[0], mf.pivotAxlePos);

                youTurnPhase = 1;
                return true;
            }
            //step 2, create the turn out fron the turnline into the next AB
            if (youTurnPhase == 1)
            {
                //timer.Start();
                //now we need to find the turn in point on the next AB-line
                double head = mf.ABLine.abHeading;
                if (!isHeadingSameWay) head += Math.PI;
                if (head >= glm.twoPI) head -= glm.twoPI;

                double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth
                    + (isYouTurnRight ? -mf.tool.offset * 2.0 : mf.tool.offset * 2.0);

                //we move the turnline crossing point perpenicualar out from the ABline
                CDubins.turningRadius = turnOffset;
                vec2 pointpos;
                if (!isTurnLeft)
                {
                    pointpos = DubinsMath.GetRightCircleCenterPos(new vec2(inClosestTurnPt.closePt.easting,
                        inClosestTurnPt.closePt.northing), head);
                }
                else
                {
                    pointpos = DubinsMath.GetLeftCircleCenterPos(new vec2(inClosestTurnPt.closePt.easting,
                        inClosestTurnPt.closePt.northing), head);
                }

                vec3 pointPos = new vec3(pointpos.easting, pointpos.northing, head);

                //step 1 if point is outside back up until inside boundary
                int stopIfWayOut = 0;
                double cosHead = Math.Cos(head) * 3;
                double sinHead = Math.Sin(head) * 3;
                while (mf.bnd.IsPointInsideTurnArea(pointPos) != 0)
                {
                    stopIfWayOut++;
                    pointPos.easting -= sinHead;
                    pointPos.northing -= cosHead;
                    if (stopIfWayOut == 200)
                    {
                        FailCreate();
                        return false;
                    }
                }

                //step 2 if pont is inside move forward until it's outside turnboundary
                cosHead = Math.Cos(head) * 3;
                sinHead = Math.Sin(head) * 3;
                while (mf.bnd.IsPointInsideTurnArea(pointPos) == 0)
                {
                    pointPos.easting += sinHead;
                    pointPos.northing += cosHead;
                }
                pointPos.heading = head;

                //step 3 create half cirkle in new list
                ytList2?.Clear();
                ytList2.Add(pointPos);
                //CDubins.turningRadius = youTurnRadius;
                //Taken from Dubbins
                while (Math.Abs(head - pointPos.heading) < Math.PI)
                {
                    //Update the position of the car
                    pointPos.easting += pointSpacing * Math.Sin(pointPos.heading);
                    pointPos.northing += pointSpacing * Math.Cos(pointPos.heading);

                    //Which way are we turning?
                    double turnParameter = 1.0;

                    if (!isTurnLeft) turnParameter = -1.0; //now we turn "the wrong" way

                    //Update the heading
                    pointPos.heading += (pointSpacing / youTurnRadius) * turnParameter;

                    //Add the new coordinate to the path
                    ytList2.Add(pointPos);
                }

                //move the half circle to tangent the turnline
                ytList2 = MoveABTurnInsideTurnLine(ytList2, head);

                if (ytList2.Count < 5 || semiCircleIndex == -1)
                {
                    FailCreate();
                    return false;
                }

                //we need to delete the points after the point that is outside
                //we need to turn the heading of the poinnts since they were created the wrong way,
                //and delete the point after the one that is out of turnbnd
                int cnt = ytList2.Count;
                vec3[] arr23 = new vec3[cnt];
                ytList2.CopyTo(arr23);
                ytList2.Clear();

                for (int i = 0; i < cnt; i++)
                {
                    if (i == semiCircleIndex)
                        break;
                    arr23[i].heading += Math.PI;
                    if (arr23[i].heading >= glm.twoPI) arr23[i].heading -= glm.twoPI;
                    else if (arr23[i].heading < 0) arr23[i].heading += glm.twoPI;
                    ytList2.Add(arr23[i]);
                }

                youTurnPhase = 2;
                return true;
            }

            //step 3, bind the turns together with help of the turnline
            if (youTurnPhase == 2)
            {
                //timer.Start();
                int cnt1 = ytList.Count;
                int cnt2 = ytList2.Count;

                //finds out start and goal point along the tunline
                FindClosestTurnPoint(ytList[cnt1 - 1]);
                inClosestTurnPt = new CClose(closestTurnPt);

                FindClosestTurnPoint(ytList2[cnt2 - 1]);
                outClosestTurnPt = new CClose(closestTurnPt);

                //we have 2 different turnLine crossings
                if (inClosestTurnPt.turnLineNum != outClosestTurnPt.turnLineNum)
                {
                    FailCreate();
                    return false;
                }

                vec3 startPoint = inClosestTurnPt.closePt;
                vec3 goalPoint = outClosestTurnPt.closePt;

                //segment index is the "A" of the segment. turnLineIndex+1 would be the "B"

                //is in and out on same segment? so only 1 segment
                if (inClosestTurnPt.turnLineIndex == outClosestTurnPt.turnLineIndex)
                {
                    for (int a = 0; a < cnt2; cnt2--)
                    {
                        ytList.Add(new vec3(ytList2[cnt2 - 1]));
                    }

                }
                else
                {
                    //multiple segments
                    bool isTurnLineSameWay = true;
                    if (Math.Abs(inClosestTurnPt.closePt.heading - ytList[cnt1 - 1].heading) > glm.PIBy2)
                        isTurnLineSameWay = false;

                    vec3 tPoint = new vec3();
                    int turnCount = mf.bnd.bndList[inClosestTurnPt.turnLineNum].turnLine.Count;

                    //how many points from turnline do we add
                    int loops = Math.Abs(inClosestTurnPt.turnLineIndex - outClosestTurnPt.turnLineIndex);

                    //are we crossing a border?
                    if (loops > (mf.bnd.bndList[inClosestTurnPt.turnLineNum].turnLine.Count / 2))
                    {
                        if (inClosestTurnPt.turnLineIndex < outClosestTurnPt.turnLineIndex)
                        {
                            loops = (turnCount - outClosestTurnPt.turnLineIndex) + inClosestTurnPt.turnLineIndex;
                        }
                        else
                        {
                            loops = (turnCount - inClosestTurnPt.turnLineIndex) + outClosestTurnPt.turnLineIndex;
                        }
                    }

                    //count up - start with B which is next A
                    if (isTurnLineSameWay)
                    {
                        for (int i = 0; i < loops; i++)
                        {
                            if ((inClosestTurnPt.turnLineIndex + 1) >= turnCount) inClosestTurnPt.turnLineIndex = -1;

                            tPoint = mf.bnd.bndList[inClosestTurnPt.turnLineNum].turnLine[inClosestTurnPt.turnLineIndex + 1];
                            inClosestTurnPt.turnLineIndex++;
                            if (inClosestTurnPt.turnLineIndex >= turnCount)
                                inClosestTurnPt.turnLineIndex = 0;
                            ytList.Add(tPoint);
                        }
                    }
                    else //count down = start with A
                    {
                        for (int i = 0; i < loops; i++)
                        {
                            tPoint = mf.bnd.bndList[inClosestTurnPt.turnLineNum].turnLine[inClosestTurnPt.turnLineIndex];
                            inClosestTurnPt.turnLineIndex--;
                            if (inClosestTurnPt.turnLineIndex == -1)
                                inClosestTurnPt.turnLineIndex = turnCount - 1;
                            ytList.Add(tPoint);
                        }
                    }

                    //add the out from ytList2
                    for (int a = 0; a < cnt2; cnt2--)
                    {
                        ytList.Add(new vec3(ytList2[cnt2 - 1]));
                    }
                }

                //fill in points, do headings

                isHeadingSameWay = mf.ABLine.isHeadingSameWay;
                double heady = mf.ABLine.abHeading;
                if (isHeadingSameWay) heady += Math.PI;
                if (heady >= glm.twoPI) heady -= glm.twoPI;

                AddSequenceLines(heady);
                if (isTurnCreationTooClose)
                {
                    FailCreate();
                    return false;
                }

                //fill in the gaps
                double distance;

                int cnt = ytList.Count;
                for (int i = 1; i < cnt - 2; i++)
                {
                    int j = i + 1;
                    if (j == cnt - 1) continue;
                    distance = glm.DistanceSquared(ytList[i], ytList[j]);
                    if (distance > 1)
                    {
                        vec3 pointB = new vec3((ytList[i].easting + ytList[j].easting) / 2.0,
                            (ytList[i].northing + ytList[j].northing) / 2.0, ytList[i].heading);

                        ytList.Insert(j, pointB);
                        cnt = ytList.Count;
                        i--;
                    }
                }

                //calculate the new points headings based on fore and aft of point - smoother turns
                cnt = ytList.Count;
                vec3[] arr = new vec3[cnt];
                cnt -= 2;
                ytList.CopyTo(arr);
                ytList.Clear();

                for (int i = 2; i < cnt; i++)
                {
                    vec3 pt3 = new vec3(arr[i]);
                    pt3.heading = Math.Atan2(arr[i + 1].easting - arr[i - 1].easting,
                        arr[i + 1].northing - arr[i - 1].northing);
                    if (pt3.heading < 0) pt3.heading += glm.twoPI;
                    ytList.Add(pt3);
                }

                isOutOfBounds = false;
                youTurnPhase = 10;
                turnTooCloseTrigger = false;
                isTurnCreationTooClose = false;
                return true;
            }

            return true;
        }

        private bool CreateABLineOmegaTurn(bool isTurnLeft)
        {
            //we are doing an omega turn
            if (youTurnPhase == 0)
            {
                //how far are we from any turn boundary
                FindClosestTurnPoint(mf.pivotAxlePos);

                //or did we lose the turnLine - we are on the highway cuz we left the outer/inner turn boundary
                if (closestTurnPt.turnLineIndex != -1)
                {
                    //calculate the distance to the turnline
                    mf.distancePivotToTurnLine = glm.Distance(mf.pivotAxlePos, closestTurnPt.closePt);
                }
                else
                {
                    //Full emergency stop code goes here, it thinks its auto turn, but its not!
                    FailCreate();
                    return false;
                }

                CDubins dubYouTurnPath = new CDubins();
                CDubins.turningRadius = youTurnRadius;

                isHeadingSameWay = mf.ABLine.isHeadingSameWay;
                double head = mf.ABLine.abHeading;

                //grab the vehicle widths and offsets
                double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth + (isYouTurnRight ? -mf.tool.offset * 2.0 : mf.tool.offset * 2.0);

                if (!isHeadingSameWay) head += Math.PI;
                if (head >= glm.twoPI) head -= glm.twoPI;

                vec3 start = new vec3(closestTurnPt.closePt);
                start.heading = head;

                vec3 goal = new vec3(start);

                //now we go the other way to turn round
                head -= Math.PI;
                if (head < -Math.PI) head += glm.twoPI;
                if (head > Math.PI) head -= glm.twoPI;

                if (isTurnLeft)
                {
                    goal.easting = goal.easting + (Math.Cos(-head) * turnOffset);
                    goal.northing = goal.northing + (Math.Sin(-head) * turnOffset);
                }
                else
                {
                    goal.easting = goal.easting - (Math.Cos(-head) * turnOffset);
                    goal.northing = goal.northing - (Math.Sin(-head) * turnOffset);
                }

                goal.heading = head;

                //generate the turn points
                ytList = dubYouTurnPath.GenerateDubins(start, goal);

                if (ytList.Count == 0)
                {
                    FailCreate();
                    return false;
                }

                else youTurnPhase = 1;

                //too many points from Dubins - so cut
                double pointSpacing = youTurnRadius * 0.1;

                double distance;

                int cnt = ytList.Count;
                for (int i = 1; i < cnt - 2; i++)
                {
                    distance = glm.DistanceSquared(ytList[i], ytList[i + 1]);
                    if (distance < pointSpacing)
                    {
                        ytList.RemoveAt(i + 1);
                        i--;
                        cnt = ytList.Count;
                    }
                }

            }

            if (youTurnPhase == 1)
            {
                //move out
                MoveABTurnInsideTurnLine(ytList, ytList[0].heading);

                if (ytList.Count == 0)
                {
                    FailCreate();
                    return false;
                }
                AddSequenceLines(ytList[0].heading - Math.PI);
                youTurnPhase = 10;
            }

            return true;
        }

        public bool KStyleTurnAB(bool isTurnLeft)
        {
            double pointSpacing = youTurnRadius * 0.1;

            int turnIndex = mf.bnd.IsPointInsideTurnArea(mf.pivotAxlePos);
            if (turnIndex != 0 || mf.makeUTurnCounter < 20)
            {
                youTurnPhase = 0;
                return true;
            }

            mf.makeUTurnCounter = 0;

            //step 1 turn in to the turnline
            if (youTurnPhase == 0)
            {
                isOutOfBounds = true;


                //timer.Start();
                //grab the pure pursuit point right on ABLine
                vec3 onPurePoint = new vec3(mf.ABLine.rEastAB, mf.ABLine.rNorthAB, 0);

                //how far are we from any turn boundary
                FindClosestTurnPoint(onPurePoint);

                //save a copy for first point
                inClosestTurnPt = new CClose(closestTurnPt);

                //already no turnline
                if (inClosestTurnPt.turnLineIndex == -1) return false;

                //calculate the distance to the turnline
                mf.distancePivotToTurnLine = glm.Distance(mf.pivotAxlePos, closestTurnPt.closePt);

                //point on AB line closest to pivot axle point from ABLine PurePursuit aka where we are
                rEastYT = mf.ABLine.rEastAB;
                rNorthYT = mf.ABLine.rNorthAB;
                isHeadingSameWay = mf.ABLine.isHeadingSameWay;
                double head = mf.ABLine.abHeading;

                if (!isHeadingSameWay) head += Math.PI;
                if (head >= glm.twoPI) head -= glm.twoPI;

                //thistance to turnline from where we are
                double turnDiagDistance = mf.distancePivotToTurnLine;

                //moves the point to the crossing with the turnline
                rEastYT += (Math.Sin(head) * turnDiagDistance);
                rNorthYT += (Math.Cos(head) * turnDiagDistance);

                //creates half a circle starting at the crossing point
                ytList.Clear();
                vec3 currentPos = new vec3(rEastYT, rNorthYT, head);
                ytList.Add(currentPos);

                //make semi circle - not quite
                while (Math.Abs(head - currentPos.heading) < 2.2)
                {
                    //Update the position of the car
                    currentPos.easting += pointSpacing * Math.Sin(currentPos.heading);
                    currentPos.northing += pointSpacing * Math.Cos(currentPos.heading);

                    //Which way are we turning?
                    double turnParameter = 1.0;

                    if (isTurnLeft) turnParameter = -1.0;

                    //Update the heading
                    currentPos.heading += (pointSpacing / youTurnRadius) * turnParameter;

                    //Add the new coordinate to the path
                    ytList.Add(currentPos);
                }

                //move the half circle to tangent the turnline
                ytList = MoveABTurnInsideTurnLine(ytList, head);

                //if it couldn't be done this will trigger
                if (ytList.Count < 5 || semiCircleIndex == -1)
                {
                    FailCreate();
                    return false;
                }

                //point used to set next guidance line
                pt3TurnNewAB = new vec3(ytList[0]);


                //grab the vehicle widths and offsets
                double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth + (isYouTurnRight ? -mf.tool.offset : mf.tool.offset);

                //now we go the other way to turn round
                head = ytList[0].heading;
                head -= Math.PI;
                if (head < -Math.PI) head += glm.twoPI;
                if (head > Math.PI) head -= glm.twoPI;

                if (isTurnLeft)
                {
                    pt3TurnNewAB.easting += (Math.Cos(-head) * turnOffset);
                    pt3TurnNewAB.northing += (Math.Sin(-head) * turnOffset);
                }
                else
                {
                    pt3TurnNewAB.easting -= (Math.Cos(-head) * turnOffset);
                    pt3TurnNewAB.northing -= (Math.Sin(-head) * turnOffset);
                }

                if (head >= glm.twoPI) head -= glm.twoPI;
                else if (head < 0) head += glm.twoPI;

                pt3TurnNewAB.heading = head;

                //add the tail to first turn
                int count = ytList.Count;
                head = ytList[count - 1].heading;

                vec3 pt;
                for (int i = 1; i <= (int)(3*turnOffset); i++)
                {
                    pt.easting = ytList[count - 1].easting + (Math.Sin(head) * i * 0.5);
                    pt.northing = ytList[count - 1].northing + (Math.Cos(head) * i * 0.5);
                    pt.heading = 0;
                    ytList.Add(pt);
                }

                //leading in line of turn
                head = mf.ABLine.abHeading;
                if (isHeadingSameWay) head += Math.PI;
                if (head >= glm.twoPI) head -= glm.twoPI;

                for (int a = 0; a < 8; a++)
                {
                    pt.easting = ytList[0].easting + (Math.Sin(head) * 0.511);
                    pt.northing = ytList[0].northing + (Math.Cos(head) * 0.511);
                    pt.heading = ytList[0].heading;
                    ytList.Insert(0, pt);
                }



                //calculate line headings
                vec3[] arr = new vec3[ytList.Count];
                ytList.CopyTo(arr);
                ytList.Clear();

                //headings of line one
                for (int i = 0; i < arr.Length - 1; i++)
                {
                    arr[i].heading = Math.Atan2(arr[i + 1].easting - arr[i].easting, arr[i + 1].northing - arr[i].northing);
                    if (arr[i].heading < 0) arr[i].heading += glm.twoPI;
                    ytList.Add(arr[i]);
                }

                mf.distancePivotToTurnLine = glm.Distance(ytList[0], mf.pivotAxlePos);

                isOutOfBounds = false;
                youTurnPhase = 10;
                turnTooCloseTrigger = false;
                isTurnCreationTooClose = false;
                return true;
            }

            return true;
        }

        #endregion

        public void FindClosestTurnPoint(vec3 fromPt)
        {
            double eP = fromPt.easting;
            double nP = fromPt.northing;
            double eAB, nAB;
            turnClosestList?.Clear();

            CClose cClose;

            if (mf.ABLine.isHeadingSameWay)
            {
                eAB = mf.ABLine.currentLinePtB.easting;
                nAB = mf.ABLine.currentLinePtB.northing;
            }
            else
            {
                eAB = mf.ABLine.currentLinePtA.easting;
                nAB = mf.ABLine.currentLinePtA.northing;
            }

            turnClosestList.Clear();

            for (int j = 0; j < mf.bnd.bndList.Count; j++)
            {
                for (int i = 0; i < mf.bnd.bndList[j].turnLine.Count - 1; i++)
                {
                    int res = GetLineIntersection(
                        mf.bnd.bndList[j].turnLine[i].easting,
                        mf.bnd.bndList[j].turnLine[i].northing,
                        mf.bnd.bndList[j].turnLine[i + 1].easting,
                        mf.bnd.bndList[j].turnLine[i + 1].northing,
                        eP, nP, eAB, nAB, ref iE, ref iN
                    );

                    if (res == 1)
                    {
                        cClose = new CClose();
                        cClose.closePt.easting = iE;
                        cClose.closePt.northing = iN;

                        double hed = Math.Atan2(mf.bnd.bndList[j].turnLine[i + 1].easting - mf.bnd.bndList[j].turnLine[i].easting,
                            mf.bnd.bndList[j].turnLine[i + 1].northing - mf.bnd.bndList[j].turnLine[i].northing);
                        if (hed < 0) hed += glm.twoPI;
                        cClose.closePt.heading = hed;
                        cClose.turnLineNum = j;
                        cClose.turnLineIndex = i;

                        turnClosestList.Add(new CClose(cClose));
                    }
                }
            }

            //determine closest point
            double minDistance = double.MaxValue;

            if (turnClosestList.Count > 0)
            {
                for (int i = 0; i < turnClosestList.Count; i++)
                {
                    double dist = (((fromPt.easting - turnClosestList[i].closePt.easting) * (fromPt.easting - turnClosestList[i].closePt.easting))
                                    + ((fromPt.northing - turnClosestList[i].closePt.northing) * (fromPt.northing - turnClosestList[i].closePt.northing)));

                    if (minDistance >= dist)
                    {

                        minDistance = dist;
                        closestTurnPt = new CClose(turnClosestList[i]);
                    }
                }
            }
        }

        private List<vec3> MoveABTurnInsideTurnLine(List<vec3> uTurnList, double head)
        {
            //step 1 make array out of the list so that we can modify the position
            double cosHead = Math.Cos(head);
            double sinHead = Math.Sin(head);
            int cnt = uTurnList.Count;
            vec3[] arr2 = new vec3[cnt];
            uTurnList.CopyTo(arr2);
            uTurnList.Clear();

            semiCircleIndex = -1;
            //step 2 move the turn inside with steps of 1 meter
            bool pointOutOfBnd = true;
            int j = 0;
            int stopIfWayOut = 0;
            while (pointOutOfBnd)
            {
                stopIfWayOut++;
                pointOutOfBnd = false;
                mf.distancePivotToTurnLine = glm.DistanceSquared(arr2[0], mf.pivotAxlePos);

                for (int i = 0; i < cnt; i++)
                {
                    arr2[i].easting -= (sinHead);
                    arr2[i].northing -= (cosHead);
                }

                for (; j < cnt; j += 1)
                {
                    if (mf.bnd.IsPointInsideTurnArea(arr2[j]) != 0)
                    {
                        pointOutOfBnd = true;
                        if (j > 0) j--;
                        break;
                    }
                }

                if (stopIfWayOut == 1000 || (mf.distancePivotToTurnLine < 6))
                {
                    //for some reason it doesn't go inside boundary, return empty list
                    return uTurnList;
                }
            }

            //step 5, we ar now inside turnfence by 0-0.1 meters, move the turn forward until it hits the turnfence in steps of 0.05 meters
            while (!pointOutOfBnd)
            {

                for (int i = 0; i < cnt; i++)
                {
                    arr2[i].easting += (sinHead * 0.1);
                    arr2[i].northing += (cosHead * 0.1);
                }

                for (int a = 0; a < cnt; a++)
                {
                    if (mf.bnd.IsPointInsideTurnArea(arr2[a]) != 0)
                    {
                        semiCircleIndex = a;
                        uTurnList.AddRange(arr2);
                        return uTurnList;
                    }
                }
            }

            //if empty - no creation.
            return uTurnList;

            //step 6, we have now placed the turn, so send it back in a list

        }

        #region Manual Turns

        public void BuildManualYouLateral(bool isTurnLeft)
        {
            double head;
            //point on AB line closest to pivot axle point from ABLine PurePursuit
            if (mf.trk.idx > -1 && mf.trk.gArr.Count > 0)
            {
                if (mf.trk.gArr[mf.trk.idx].mode == (int)TrackMode.AB)
                {
                    rEastYT = mf.ABLine.rEastAB;
                    rNorthYT = mf.ABLine.rNorthAB;
                    isHeadingSameWay = mf.ABLine.isHeadingSameWay;
                    head = mf.ABLine.abHeading;
                    mf.ABLine.isLateralTriggered = true;
                }
                else
                {
                    rEastYT = mf.curve.rEastCu;
                    rNorthYT = mf.curve.rNorthCu;
                    isHeadingSameWay = mf.curve.isHeadingSameWay;
                    head = mf.curve.manualUturnHeading;
                    mf.curve.isLateralTriggered = true;
                }
            }
            else return;

            //grab the vehicle widths and offsets
            double turnOffset = (mf.tool.width - mf.tool.overlap); //remove rowSkips

            //if its straight across it makes 2 loops instead so goal is a little lower then start
            if (!isHeadingSameWay) head += Math.PI;

            //move the start forward 2 meters, this point is critical to formation of uturn
            rEastYT += (Math.Sin(head) * 2);
            rNorthYT += (Math.Cos(head) * 2);

            if (isTurnLeft)
            {
                mf.guidanceLookPos.easting = rEastYT + (Math.Cos(-head) * turnOffset);
                mf.guidanceLookPos.northing = rNorthYT + (Math.Sin(-head) * turnOffset);
            }
            else
            {
                mf.guidanceLookPos.easting = rEastYT - (Math.Cos(-head) * turnOffset);
                mf.guidanceLookPos.northing = rNorthYT - (Math.Sin(-head) * turnOffset);
            }

            mf.ABLine.isABValid = false;
            mf.curve.isCurveValid = false;
        }

        //build the points and path of youturn to be scaled and transformed
        public void BuildManualYouTurn(bool isTurnLeft, bool isTurnButtonTriggered)
        {
            isYouTurnTriggered = true;

            double head;
            //point on AB line closest to pivot axle point from ABLine PurePursuit
            if (mf.trk.idx > -1 && mf.trk.gArr.Count > 0)
            {
                if (mf.trk.gArr[mf.trk.idx].mode == (int)TrackMode.AB)
                {
                    rEastYT = mf.ABLine.rEastAB;
                    rNorthYT = mf.ABLine.rNorthAB;
                    isHeadingSameWay = mf.ABLine.isHeadingSameWay;
                    head = mf.ABLine.abHeading;
                    mf.ABLine.isLateralTriggered = true;
                }

                else
                {
                    rEastYT = mf.curve.rEastCu;
                    rNorthYT = mf.curve.rNorthCu;
                    isHeadingSameWay = mf.curve.isHeadingSameWay;
                    head = mf.curve.manualUturnHeading;
                    mf.curve.isLateralTriggered = true;
                }
            }
            else return;

            //grab the vehicle widths and offsets
            double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth + (isTurnLeft ? mf.tool.offset * 2.0 : -mf.tool.offset * 2.0);

            CDubins dubYouTurnPath = new CDubins();
            CDubins.turningRadius = youTurnRadius;

            //if its straight across it makes 2 loops instead so goal is a little lower then start
            if (!isHeadingSameWay) head += 3.14;
            else head -= 0.01;

            //move the start forward 2 meters, this point is critical to formation of uturn
            rEastYT += (Math.Sin(head) * 4);
            rNorthYT += (Math.Cos(head) * 4);

            //now we have our start point
            vec3 start = new vec3(rEastYT, rNorthYT, head);
            vec3 goal = new vec3();

            //now we go the other way to turn round
            head -= Math.PI;
            if (head < 0) head += glm.twoPI;

            //set up the goal point for Dubins
            goal.heading = head;
            if (isTurnButtonTriggered)
            {
                if (isTurnLeft)
                {
                    goal.easting = rEastYT - (Math.Cos(-head) * turnOffset);
                    goal.northing = rNorthYT - (Math.Sin(-head) * turnOffset);
                }
                else
                {
                    goal.easting = rEastYT + (Math.Cos(-head) * turnOffset);
                    goal.northing = rNorthYT + (Math.Sin(-head) * turnOffset);
                }
            }

            //generate the turn points
            ytList = dubYouTurnPath.GenerateDubins(start, goal);

            mf.guidanceLookPos.easting = ytList[ytList.Count - 1].easting;
            mf.guidanceLookPos.northing = ytList[ytList.Count - 1].northing;

            //vec3 pt;
            //for (double a = 0; a < 2; a += 0.2)
            //{
            //    pt.easting = ytList[0].easting + (Math.Sin(head) * a);
            //    pt.northing = ytList[0].northing + (Math.Cos(head) * a);
            //    pt.heading = ytList[0].heading;
            //    ytList.Insert(0, pt);
            //}

            //int count = ytList.Count;

            //for (double i = 0.2; i <= 7; i += 0.2)
            //{
            //    pt.easting = ytList[count - 1].easting + (Math.Sin(head) * i);
            //    pt.northing = ytList[count - 1].northing + (Math.Cos(head) * i);
            //    pt.heading = head;
            //    ytList.Add(pt);
            //}

            mf.ABLine.isABValid = false;
            mf.curve.isCurveValid = false;
        }

        #endregion

        public int GetLineIntersection(double p0x, double p0y, double p1x, double p1y,
                double p2x, double p2y, double p3x, double p3y, ref double iEast, ref double iNorth)
        {
            double s1x, s1y, s2x, s2y;
            s1x = p1x - p0x;
            s1y = p1y - p0y;

            s2x = p3x - p2x;
            s2y = p3y - p2y;

            double s, t;
            s = (-s1y * (p0x - p2x) + s1x * (p0y - p2y)) / (-s2x * s1y + s1x * s2y);

            if (s >= 0 && s <= 1)
            {
                //check oher side
                t = (s2x * (p0y - p2y) - s2y * (p0x - p2x)) / (-s2x * s1y + s1x * s2y);
                if (t >= 0 && t <= 1)
                {
                    // Collision detected
                    iEast = p0x + (t * s1x);
                    iNorth = p0y + (t * s1y);
                    return 1;
                }
            }

            return 0; // No collision
        }

        public void AddSequenceLines(double head)
        {
            //how many points striaght out
            double lenny = 8;

            vec3 pt;
            for (int a = 0; a < lenny; a++)
            {
                pt.easting = ytList[0].easting + (Math.Sin(head) * 0.511);
                pt.northing = ytList[0].northing + (Math.Cos(head) * 0.511);
                pt.heading = ytList[0].heading;
                ytList.Insert(0, pt);
            }

            int count = ytList.Count;

            for (int i = 1; i <= lenny; i++)
            {
                pt.easting = ytList[count - 1].easting + (Math.Sin(head) * i * 0.511);
                pt.northing = ytList[count - 1].northing + (Math.Cos(head) * i * 0.511);
                pt.heading = head;
                ytList.Add(pt);
            }

            double distancePivotToTurnLine;
            count = ytList.Count;
            for (int i = 0; i < count; i += 2)
            {
                distancePivotToTurnLine = glm.DistanceSquared(ytList[i], mf.pivotAxlePos);
                if (distancePivotToTurnLine > 6)
                {
                    isTurnCreationTooClose = false;
                }
                else
                {
                    //set the flag to Critical stop machine
                    FailCreate();
                    break;
                }
            }
        }

        public void FailCreate()
        {
            //fail
            isTurnCreationTooClose = true;
            mf.mc.isOutOfBounds = true;
            youTurnPhase = 11;
        }

        public void SmoothYouTurn(int smPts)
        {
            //count the reference list of original curve
            int cnt = ytList.Count;

            //the temp array
            vec3[] arr = new vec3[cnt];

            //read the points before and after the setpoint
            for (int s = 0; s < smPts / 2; s++)
            {
                arr[s].easting = ytList[s].easting;
                arr[s].northing = ytList[s].northing;
                arr[s].heading = ytList[s].heading;
            }

            for (int s = cnt - (smPts / 2); s < cnt; s++)
            {
                arr[s].easting = ytList[s].easting;
                arr[s].northing = ytList[s].northing;
                arr[s].heading = ytList[s].heading;
            }

            //average them - center weighted average
            for (int i = smPts / 2; i < cnt - (smPts / 2); i++)
            {
                for (int j = -smPts / 2; j < smPts / 2; j++)
                {
                    arr[i].easting += ytList[j + i].easting;
                    arr[i].northing += ytList[j + i].northing;
                }
                arr[i].easting /= smPts;
                arr[i].northing /= smPts;
                arr[i].heading = ytList[i].heading;
            }

            ytList?.Clear();

            //calculate new headings on smoothed line
            for (int i = 1; i < cnt - 1; i++)
            {
                arr[i].heading = Math.Atan2(arr[i + 1].easting - arr[i].easting, arr[i + 1].northing - arr[i].northing);
                if (arr[i].heading < 0) arr[i].heading += glm.twoPI;
                ytList.Add(arr[i]);
            }
        }

        //called to initiate turn
        public void YouTurnTrigger()
        {
            //trigger pulled
            isYouTurnTriggered = true;

            if (alternateSkips && rowSkipsWidth2 > 1)
            {
                if (--turnSkips == 0)
                {
                    isYouTurnRight = !isYouTurnRight;
                    turnSkips = rowSkipsWidth2 * 2 - 1;
                }
                else if (previousBigSkip = !previousBigSkip)
                    rowSkipsWidth = rowSkipsWidth2 - 1;
                else
                    rowSkipsWidth = rowSkipsWidth2;
            }
            else isYouTurnRight = !isYouTurnRight;

            if (uTurnStyle == 0)
            {
                mf.guidanceLookPos.easting = ytList[ytList.Count - 1].easting;
                mf.guidanceLookPos.northing = ytList[ytList.Count - 1].northing;
            }
            else if (uTurnStyle == 1)
            {
                mf.guidanceLookPos.easting = pt3TurnNewAB.easting;
                mf.guidanceLookPos.northing = pt3TurnNewAB.northing;

                pt3Phase = 0;
            }

            if (mf.trk.idx > -1 && mf.trk.gArr.Count > 0)
            {
                if (mf.trk.gArr[mf.trk.idx].mode == (int)TrackMode.AB)
                {
                    mf.ABLine.isLateralTriggered = true;
                    mf.ABLine.isABValid = false;
                }
                else
                {
                    mf.curve.isLateralTriggered = true;
                    mf.curve.isCurveValid = false;
                }
            }
        }

        //Normal copmpletion of youturn
        public void CompleteYouTurn()
        {
            isYouTurnTriggered = false;
            ResetCreatedYouTurn();
            mf.sounds.isBoundAlarming = false;
        }

        public void Set_Alternate_skips()
        {
            rowSkipsWidth2 = rowSkipsWidth;
            turnSkips = rowSkipsWidth2 * 2 - 1;
            previousBigSkip = false;
        }

        //something went seriously wrong so reset everything
        public void ResetYouTurn()
        {
            //fix you turn
            isYouTurnTriggered = false;
            mf.makeUTurnCounter = 0;
            ytList?.Clear();
            ResetCreatedYouTurn();
            mf.sounds.isBoundAlarming = false;
            isTurnCreationTooClose = false;
            isTurnCreationNotCrossingError = false;
            mf.p_239.pgn[mf.p_239.uturn] = 0;
        }

        public void ResetCreatedYouTurn()
        {
            youTurnPhase = 0;
            ytList?.Clear();
            pt3Phase = 0;
            mf.makeUTurnCounter = 0;
            mf.p_239.pgn[mf.p_239.uturn] = 0;
        }

        //determine distance from youTurn guidance line
        public bool DistanceFromYouTurnLine()
        {
            //grab a copy from main - the steer position
            double minDistA = 1000000, minDistB = 1000000;
            int ptCount = ytList.Count;

            if (ptCount > 0)
            {
                if (mf.isStanleyUsed)
                {
                    pivot = mf.steerAxlePos;

                    //find the closest 2 points to current fix
                    for (int t = 0; t < ptCount; t++)
                    {
                        double dist = ((pivot.easting - ytList[t].easting) * (pivot.easting - ytList[t].easting))
                                        + ((pivot.northing - ytList[t].northing) * (pivot.northing - ytList[t].northing));
                        if (dist < minDistA)
                        {
                            minDistB = minDistA;
                            B = A;
                            minDistA = dist;
                            A = t;
                        }
                        else if (dist < minDistB)
                        {
                            minDistB = dist;
                            B = t;
                        }
                    }

                    if (minDistA > 16)
                    {
                        CompleteYouTurn();
                        return false;
                    }

                    //just need to make sure the points continue ascending or heading switches all over the place
                    if (A > B)
                    {
                        (B, A) = (A, B);
                    }

                    //minDistA = 100;
                    //int closestPt = 0;
                    //for (int i = 0; i < ptCount; i++)
                    //{
                    //    double distancePiv = glm.DistanceSquared(ytList[i], pivot);
                    //    if (distancePiv < minDistA)
                    //    {
                    //        minDistA = distancePiv;
                    //    }
                    //}

                    //feed backward to turn slower to keep pivot on
                    A -= 7;
                    if (A < 0)
                    {
                        A = 0;
                    }
                    B = A + 1;

                    //return and reset if too far away or end of the line
                    if (B >= ptCount - 8)
                    {
                        CompleteYouTurn();
                        return false;
                    }

                    //get the distance from currently active AB line, precalc the norm of line
                    double dx = ytList[B].easting - ytList[A].easting;
                    double dz = ytList[B].northing - ytList[A].northing;
                    if (Math.Abs(dx) < Double.Epsilon && Math.Abs(dz) < Double.Epsilon) return false;

                    double abHeading = ytList[A].heading;

                    //how far from current AB Line is steer point 90 degrees from steer position
                    distanceFromCurrentLine = ((dz * pivot.easting) - (dx * pivot.northing) + (ytList[B].easting
                                * ytList[A].northing) - (ytList[B].northing * ytList[A].easting))
                                    / Math.Sqrt((dz * dz) + (dx * dx));

                    //Calc point on ABLine closest to current position and 90 degrees to segment heading
                    double U = (((pivot.easting - ytList[A].easting) * dx)
                                + ((pivot.northing - ytList[A].northing) * dz))
                                / ((dx * dx) + (dz * dz));

                    //critical point used as start for the uturn path - critical
                    rEastYT = ytList[A].easting + (U * dx);
                    rNorthYT = ytList[A].northing + (U * dz);

                    //the first part of stanley is to extract heading error
                    double abFixHeadingDelta = (pivot.heading - abHeading);

                    //Fix the circular error - get it from -Pi/2 to Pi/2
                    if (abFixHeadingDelta > Math.PI) abFixHeadingDelta -= Math.PI;
                    else if (abFixHeadingDelta < Math.PI) abFixHeadingDelta += Math.PI;
                    if (abFixHeadingDelta > glm.PIBy2) abFixHeadingDelta -= Math.PI;
                    else if (abFixHeadingDelta < -glm.PIBy2) abFixHeadingDelta += Math.PI;

                    if (mf.isReverse) abFixHeadingDelta *= -1;
                    //normally set to 1, less then unity gives less heading error.
                    abFixHeadingDelta *= mf.vehicle.stanleyHeadingErrorGain;
                    if (abFixHeadingDelta > 0.74) abFixHeadingDelta = 0.74;
                    if (abFixHeadingDelta < -0.74) abFixHeadingDelta = -0.74;

                    //the non linear distance error part of stanley
                    steerAngleYT = Math.Atan((distanceFromCurrentLine * mf.vehicle.stanleyDistanceErrorGain) / ((mf.avgSpeed * 0.277777) + 1));

                    //clamp it to max 42 degrees
                    if (steerAngleYT > 0.74) steerAngleYT = 0.74;
                    if (steerAngleYT < -0.74) steerAngleYT = -0.74;

                    //add them up and clamp to max in vehicle settings
                    steerAngleYT = glm.toDegrees((steerAngleYT + abFixHeadingDelta) * -1.0);
                    if (steerAngleYT < -mf.vehicle.maxSteerAngle) steerAngleYT = -mf.vehicle.maxSteerAngle;
                    if (steerAngleYT > mf.vehicle.maxSteerAngle) steerAngleYT = mf.vehicle.maxSteerAngle;
                }
                else
                {
                    pivot = mf.pivotAxlePos;

                    //find the closest 2 points to current fix
                    for (int t = 0; t < ptCount; t++)
                    {
                        double dist = ((pivot.easting - ytList[t].easting) * (pivot.easting - ytList[t].easting))
                                        + ((pivot.northing - ytList[t].northing) * (pivot.northing - ytList[t].northing));
                        if (dist < minDistA)
                        {
                            minDistB = minDistA;
                            B = A;
                            minDistA = dist;
                            A = t;
                        }
                        else if (dist < minDistB)
                        {
                            minDistB = dist;
                            B = t;
                        }
                    }

                    //just need to make sure the points continue ascending or heading switches all over the place
                    if (A > B)
                    {
                        (B, A) = (A, B);
                    }

                    onA = A;
                    double distancePiv = glm.Distance(ytList[A], pivot);

                    if (distancePiv > 2 || (B >= ptCount - 1))
                    {
                        CompleteYouTurn();
                        return false;
                    }

                    //get the distance from currently active AB line
                    double dx = ytList[B].easting - ytList[A].easting;
                    double dz = ytList[B].northing - ytList[A].northing;

                    if (Math.Abs(dx) < double.Epsilon && Math.Abs(dz) < double.Epsilon) return false;

                    //how far from current AB Line is fix
                    distanceFromCurrentLine = ((dz * pivot.easting) - (dx * pivot.northing) + (ytList[B].easting
                                * ytList[A].northing) - (ytList[B].northing * ytList[A].easting))
                                    / Math.Sqrt((dz * dz) + (dx * dx));

                    // ** Pure pursuit ** - calc point on ABLine closest to current position
                    double U = (((pivot.easting - ytList[A].easting) * dx)
                                + ((pivot.northing - ytList[A].northing) * dz))
                                / ((dx * dx) + (dz * dz));

                    rEastYT = ytList[A].easting + (U * dx);
                    rNorthYT = ytList[A].northing + (U * dz);

                    //sharp turns on you turn.
                    //update base on autosteer settings and distance from line
                    double goalPointDistance = 0.8 * mf.vehicle.UpdateGoalPointDistance();

                    isHeadingSameWay = true;
                    bool ReverseHeading = !mf.isReverse;

                    int count = ReverseHeading ? 1 : -1;
                    vec3 start = new vec3(rEastYT, rNorthYT, 0);
                    double distSoFar = 0;

                    for (int i = ReverseHeading ? B : A; i < ptCount && i >= 0; i += count)
                    {
                        // used for calculating the length squared of next segment.
                        double tempDist = glm.Distance(start, ytList[i]);

                        //will we go too far?
                        if ((tempDist + distSoFar) > goalPointDistance)
                        {
                            double j = (goalPointDistance - distSoFar) / tempDist; // the remainder to yet travel

                            goalPointYT.easting = (((1 - j) * start.easting) + (j * ytList[i].easting));
                            goalPointYT.northing = (((1 - j) * start.northing) + (j * ytList[i].northing));
                            break;
                        }
                        else distSoFar += tempDist;

                        start = ytList[i];

                        if (i == ptCount - 1)//goalPointDistance is longer than remaining u-turn
                        {
                            CompleteYouTurn();
                            return false;
                        }

                        if (uTurnStyle == 1 && pt3Phase == 0 && mf.isReverse)
                        {
                            CompleteYouTurn();
                            return true;
                        }
                    }

                    //calc "D" the distance from pivot axle to lookahead point
                    double goalPointDistanceSquared = glm.DistanceSquared(goalPointYT.northing, goalPointYT.easting, pivot.northing, pivot.easting);

                    //calculate the the delta x in local coordinates and steering angle degrees based on wheelbase
                    double localHeading = glm.twoPI - mf.fixHeading;
                    ppRadiusYT = goalPointDistanceSquared / (2 * (((goalPointYT.easting - pivot.easting) * Math.Cos(localHeading)) + ((goalPointYT.northing - pivot.northing) * Math.Sin(localHeading))));

                    steerAngleYT = glm.toDegrees(Math.Atan(2 * (((goalPointYT.easting - pivot.easting) * Math.Cos(localHeading))
                        + ((goalPointYT.northing - pivot.northing) * Math.Sin(localHeading))) * mf.vehicle.wheelbase / goalPointDistanceSquared));

                    if (steerAngleYT < -mf.vehicle.maxSteerAngle) steerAngleYT = -mf.vehicle.maxSteerAngle;
                    if (steerAngleYT > mf.vehicle.maxSteerAngle) steerAngleYT = mf.vehicle.maxSteerAngle;

                    if (ppRadiusYT < -500) ppRadiusYT = -500;
                    if (ppRadiusYT > 500) ppRadiusYT = 500;

                    radiusPointYT.easting = pivot.easting + (ppRadiusYT * Math.Cos(localHeading));
                    radiusPointYT.northing = pivot.northing + (ppRadiusYT * Math.Sin(localHeading));

                    //distance is negative if on left, positive if on right
                    if (!isHeadingSameWay)
                        distanceFromCurrentLine *= -1.0;
                }

                //used for smooth mode
                mf.vehicle.modeActualXTE = (distanceFromCurrentLine);

                //Convert to centimeters
                mf.guidanceLineDistanceOff = (short)Math.Round(distanceFromCurrentLine * 1000.0, MidpointRounding.AwayFromZero);
                mf.guidanceLineSteerAngle = (short)(steerAngleYT * 100);
                return true;
            }
            else
            {
                CompleteYouTurn();
                return false;
            }
        }

        public class CClose
        {
            public vec3 closePt = new vec3();
            public int turnLineNum;
            public int turnLineIndex;
            public int curveIndex;

            public CClose()
            {
                closePt = new vec3();
                turnLineNum = -1;
                turnLineIndex = -1;
                curveIndex = -1;
            }

            public CClose(CClose _clo)
            {
                closePt = new vec3(_clo.closePt);
                turnLineNum = _clo.turnLineNum;
                turnLineIndex = _clo.turnLineIndex;
                curveIndex = _clo.curveIndex;
            }
        }
    }
}