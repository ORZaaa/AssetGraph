using UnityEngine;
using UnityEditor;

using System;
using System.Linq;
using System.Collections.Generic;

using Model=UnityEngine.AssetBundles.GraphTool.DataModel.Version2;

namespace UnityEngine.AssetBundles.GraphTool {
	[Serializable] 
	public class NodeGUI {

		[SerializeField] private int m_nodeWindowId;
		[SerializeField] private Rect m_baseRect;

		[SerializeField] private Model.NodeData m_data;
		[SerializeField] private Model.ConfigGraph m_graph;

		[SerializeField] private string m_nodeSyle;

		[SerializeField] private NodeGUIInspectorHelper m_nodeInsp;

		/*
			show error on node functions.
		*/
		private bool m_hasErrors = false;
		/*
					show progress on node functions(unused. due to mainthread synchronization problem.)
			can not update any visual on Editor while building AssetBundles through AssetBundleGraph.
		*/
		private float m_progress;
		private bool m_running;

		private Vector2 m_lastMouseDragPos = Vector2.zero;

		/*
		 * Properties
		 */
		public string Name {
			get {
				return m_data.Name;
			}
			set {
				m_data.Name = value;
			}
		}

		public string Id {
			get {
				return m_data.Id;
			}
		}

		public Model.NodeData Data {
			get {
				return m_data;
			}
		}

		public Rect Region {
			get {
				return m_baseRect;
			}
		}

		public Model.ConfigGraph ParentGraph {
			get {
				return m_graph;
			}
		}

		private NodeGUIInspectorHelper Inspector {
			get {
				if(m_nodeInsp == null) {
					m_nodeInsp = ScriptableObject.CreateInstance<NodeGUIInspectorHelper>();
					m_nodeInsp.hideFlags = HideFlags.DontSave;
					m_nodeInsp.node = this;
				}
				return m_nodeInsp;
			}
		}

		public void ResetErrorStatus () {
			m_hasErrors = false;
			Inspector.UpdateErrors(new List<string>());
		}

		public void AppendErrorSources (List<string> errors) {
			this.m_hasErrors = true;
			Inspector.UpdateErrors(errors);
		}

		public int WindowId {
			get {
				return m_nodeWindowId;
			}

			set {
				m_nodeWindowId = value;
			}
		}

		public NodeGUI (AssetBundleGraphController controller, Model.NodeData data) {
			m_nodeWindowId = 0;
			m_graph = controller.TargetGraph;
			m_data = data;

			m_baseRect = new Rect(m_data.X, m_data.Y, Model.Settings.GUI.NODE_BASE_WIDTH, Model.Settings.GUI.NODE_BASE_HEIGHT);

			m_nodeSyle = data.Operation.Object.InactiveStyle;
			Inspector.controller = controller;
		}

		public NodeGUI Duplicate (AssetBundleGraphController controller, float newX, float newY) {
			var data = m_data.Duplicate();
			data.X = newX;
			data.Y = newY;
			return new NodeGUI(controller, data);
		}

		public void SetActive (bool active) {
			if(active) {
				Selection.activeObject = Inspector;
				m_nodeSyle = m_data.Operation.Object.ActiveStyle;
			} else {
				m_nodeSyle = m_data.Operation.Object.InactiveStyle;
			}
		}

		private void RefreshConnectionPos (float yOffset) {
			for (int i = 0; i < m_data.InputPoints.Count; i++) {
				var point = m_data.InputPoints[i];
				point.UpdateRegion(this, yOffset, i, m_data.InputPoints.Count);
			}

			for (int i = 0; i < m_data.OutputPoints.Count; i++) {
				var point = m_data.OutputPoints[i];
				point.UpdateRegion(this, yOffset, i, m_data.OutputPoints.Count);
			}
		}

		private bool IsValidInputConnectionPoint(Model.ConnectionPointData point) {
			return m_data.Operation.Object.IsValidInputConnectionPoint(point);
		}

		/**
			retrieve mouse events for this node in this GraphEditor window.
		*/
		private void HandleNodeEvent () {
			switch (Event.current.type) {

			/*
					handling release of mouse drag from this node to another node.
					this node doesn't know about where the other node is. the master only knows.
					only emit event.
				*/
			case EventType.Ignore: {
					NodeGUIUtility.NodeEventHandler(new NodeEvent(NodeEvent.EventType.EVENT_CONNECTING_END, this, Event.current.mousePosition, null));
					break;
				}

				/*
					handling drag.
				*/
			case EventType.MouseDrag: {
					NodeGUIUtility.NodeEventHandler(new NodeEvent(NodeEvent.EventType.EVENT_NODE_MOVING, this, GetPos() - m_lastMouseDragPos, null));
					m_lastMouseDragPos = GetPos();
					break;
				}

				/*
					check if the mouse-down point is over one of the connectionPoint in this node.
					then emit event.
				*/
			case EventType.MouseDown: {
					m_lastMouseDragPos = GetPos();

					Model.ConnectionPointData result = IsOverConnectionPoint(Event.current.mousePosition);

					if (result != null) {
						NodeGUIUtility.NodeEventHandler(new NodeEvent(NodeEvent.EventType.EVENT_CONNECTING_BEGIN, this, Event.current.mousePosition, result));
						break;
					}
					break;
				}
			}

			/*
				retrieve mouse events for this node in|out of this GraphTool window.
			*/
			switch (Event.current.rawType) {
			case EventType.MouseUp: {
					bool eventSent = false;
					// send EVENT_CONNECTION_ESTABLISHED event if MouseUp performed on ConnectionPoint
					Action<Model.ConnectionPointData> raiseEventIfHit = (Model.ConnectionPointData point) => {
						// Only one connectionPoint can send NodeEvent.
						if(eventSent) {
							return;
						}

						// If InputConnectionPoint is not valid at this moment, ignore
						if(!IsValidInputConnectionPoint(point)) {
							return;
						}

						if (point.Region.Contains(Event.current.mousePosition)) {
							NodeGUIUtility.NodeEventHandler(
								new NodeEvent(NodeEvent.EventType.EVENT_CONNECTION_ESTABLISHED, 
									this, Event.current.mousePosition, point));
							eventSent = true;
							return;
						}
					};
					m_data.InputPoints.ForEach(raiseEventIfHit);
					m_data.OutputPoints.ForEach(raiseEventIfHit);
					if(!eventSent) {
						NodeGUIUtility.NodeEventHandler(new NodeEvent(NodeEvent.EventType.EVENT_NODE_CLICKED, 
							this, Event.current.mousePosition, null));
					}
					break;
				}
			}

			/*
				right click to open Context menu
			*/
			if (Event.current.type == EventType.ContextClick || (Event.current.type == EventType.MouseUp && Event.current.button == 1)) 
			{
				var menu = new GenericMenu();

				Data.Operation.Object.OnContextMenuGUI(menu);

				menu.AddItem(
					new GUIContent("Delete"),
					false, 
					() => {
						NodeGUIUtility.NodeEventHandler(new NodeEvent(NodeEvent.EventType.EVENT_NODE_DELETE, this, Vector2.zero, null));
					}
				);
				menu.ShowAsContext();
				Event.current.Use();
			}
		}

		public void DrawConnectionInputPointMark (NodeEvent eventSource, bool justConnecting) {
			var defaultPointTex = NodeGUIUtility.inputPointMarkTex;
			bool shouldDrawEnable = 
				!( eventSource != null && eventSource.eventSourceNode != null && 
					!Model.ConnectionData.CanConnect(eventSource.eventSourceNode.Data, m_data)
				);

			if (shouldDrawEnable && justConnecting && eventSource != null) {
				if (eventSource.eventSourceNode.Id != this.Id) {
					var connectionPoint = eventSource.point;
					if (connectionPoint.IsOutput) {
						defaultPointTex = NodeGUIUtility.enablePointMarkTex;
					}
				}
			}

			foreach (var point in m_data.InputPoints) {
				if(IsValidInputConnectionPoint(point)) {
					GUI.DrawTexture(point.GetGlobalPointRegion(this), defaultPointTex);
				}
			}
		}

		public void DrawConnectionOutputPointMark (NodeEvent eventSource, bool justConnecting, Event current) {
			var defaultPointTex = NodeGUIUtility.outputPointMarkConnectedTex;
			bool shouldDrawEnable = 
				!( eventSource != null && eventSource.eventSourceNode != null && 
					!Model.ConnectionData.CanConnect(m_data, eventSource.eventSourceNode.Data)
				);

			if (shouldDrawEnable && justConnecting && eventSource != null) {
				if (eventSource.eventSourceNode.Id != this.Id) {
					var connectionPoint = eventSource.point;
					if (connectionPoint.IsInput) {
						defaultPointTex = NodeGUIUtility.enablePointMarkTex;
					}
				}
			}

			var globalMousePosition = current.mousePosition;

			foreach (var point in m_data.OutputPoints) {
				var pointRegion = point.GetGlobalPointRegion(this);

				GUI.DrawTexture(
					pointRegion, 
					defaultPointTex
				);

				// eventPosition is contained by outputPointRect.
				if (pointRegion.Contains(globalMousePosition)) {
					if (current.type == EventType.MouseDown) {
						NodeGUIUtility.NodeEventHandler(
							new NodeEvent(NodeEvent.EventType.EVENT_CONNECTING_BEGIN, this, current.mousePosition, point));
					}
				}
			}
		}

		public void DrawNode () {
			var movedRect = GUI.Window(m_nodeWindowId, m_baseRect, DrawThisNode, string.Empty, m_nodeSyle);
			SetPos(movedRect.position);
		}

		private void DrawThisNode(int id) {
			UpdateNodeRect ();
			HandleNodeEvent ();
			DrawNodeContents();
			GUI.DragWindow();
		}
			
		private void DrawNodeContents () {
			var oldColor = GUI.color;
			var textColor = (EditorGUIUtility.isProSkin)? Color.black : oldColor;
			var style = new GUIStyle(EditorStyles.label);
			style.alignment = TextAnchor.MiddleCenter;

			var connectionNodeStyleOutput = new GUIStyle(EditorStyles.label);
			connectionNodeStyleOutput.alignment = TextAnchor.MiddleRight;

			var connectionNodeStyleInput = new GUIStyle(EditorStyles.label);
			connectionNodeStyleInput.alignment = TextAnchor.MiddleLeft;

			var titleHeight = style.CalcSize(new GUIContent(Name)).y + Model.Settings.GUI.NODE_TITLE_HEIGHT_MARGIN;
			var nodeTitleRect = new Rect(0, 0, m_baseRect.width, titleHeight);
			GUI.color = textColor;
			GUI.Label(nodeTitleRect, Name, style);
			GUI.color = oldColor;

			if (m_running) {
				EditorGUI.ProgressBar(new Rect(10f, m_baseRect.height - 20f, m_baseRect.width - 20f, 10f), m_progress, string.Empty);
			}
			if (m_hasErrors) {
				GUIStyle errorStyle = new GUIStyle("CN EntryError");
				errorStyle.alignment = TextAnchor.MiddleCenter;
				var labelSize = GUI.skin.label.CalcSize(new GUIContent(Name));
				EditorGUI.LabelField(new Rect((nodeTitleRect.width - labelSize.x )/2.0f - 28f, (nodeTitleRect.height-labelSize.y)/2.0f - 7f, 20f, 20f), string.Empty, errorStyle);
			}

			// draw & update connectionPoint button interface.
			Action<Model.ConnectionPointData> drawConnectionPoint = (Model.ConnectionPointData point) => 
			{
				var label = point.Label;
				if( label != Model.Settings.DEFAULT_INPUTPOINT_LABEL &&
					label != Model.Settings.DEFAULT_OUTPUTPOINT_LABEL) 
				{
					var region = point.Region;
					// if point is output node, then label position offset is minus. otherwise plus.
					var xOffset = (point.IsOutput) ? - m_baseRect.width : Model.Settings.GUI.INPUT_POINT_WIDTH;
					var labelStyle = (point.IsOutput) ? connectionNodeStyleOutput : connectionNodeStyleInput;
					var labelRect = new Rect(region.x + xOffset, region.y - (region.height/2), m_baseRect.width, region.height*2);

					GUI.color = textColor;
					GUI.Label(labelRect, label, labelStyle);
					GUI.color = oldColor;
				}
				GUI.backgroundColor = Color.clear;
				Texture2D tex = (point.IsInput)? NodeGUIUtility.inputPointTex : NodeGUIUtility.outputPointTex;
				GUI.Button(point.Region, tex, "AnimationKeyframeBackground");
			};
			m_data.InputPoints.ForEach(drawConnectionPoint);
			m_data.OutputPoints.ForEach(drawConnectionPoint);
		}

		public void UpdateNodeRect () {
			// UpdateNodeRect will be called outside OnGUI(), so it use inacurate but simple way to calcurate label width
			// instead of CalcSize()

			float labelWidth = GUI.skin.label.CalcSize(new GUIContent(this.Name)).x;
			float outputLabelWidth = 0f;
			float inputLabelWidth = 0f;

			if(m_data.InputPoints.Count > 0) {
				var inputLabels = m_data.InputPoints.OrderByDescending(p => p.Label.Length).Select(p => p.Label);
				if (inputLabels.Any()) {
					inputLabelWidth = GUI.skin.label.CalcSize(new GUIContent(inputLabels.First())).x;
				}
			}

			if(m_data.OutputPoints.Count > 0) {
				var outputLabels = m_data.OutputPoints.OrderByDescending(p => p.Label.Length).Select(p => p.Label);
				if (outputLabels.Any()) {
					outputLabelWidth = GUI.skin.label.CalcSize(new GUIContent(outputLabels.First())).x;
				}
			}

			var titleHeight = GUI.skin.label.CalcSize(new GUIContent(Name)).y + Model.Settings.GUI.NODE_TITLE_HEIGHT_MARGIN;

			// update node height by number of output connectionPoint.
			var nPoints = Mathf.Max(m_data.OutputPoints.Count, m_data.InputPoints.Count);
			this.m_baseRect = new Rect(m_baseRect.x, m_baseRect.y, 
				m_baseRect.width, 
				Model.Settings.GUI.NODE_BASE_HEIGHT + titleHeight + (Model.Settings.GUI.FILTER_OUTPUT_SPAN * Mathf.Max(0, (nPoints - 1)))
			);

			var newWidth = Mathf.Max(Model.Settings.GUI.NODE_BASE_WIDTH, outputLabelWidth + inputLabelWidth + Model.Settings.GUI.NODE_WIDTH_MARGIN);
			newWidth = Mathf.Max(newWidth, labelWidth + Model.Settings.GUI.NODE_WIDTH_MARGIN);
			m_baseRect = new Rect(m_baseRect.x, m_baseRect.y, newWidth, m_baseRect.height);

			RefreshConnectionPos(titleHeight);
		}

		private Model.ConnectionPointData IsOverConnectionPoint (Vector2 touchedPoint) {

			foreach(var p in m_data.InputPoints) {
				var region = p.Region;

				if(!IsValidInputConnectionPoint(p)) {
					continue;
				}

				if (region.x <= touchedPoint.x && 
					touchedPoint.x <= region.x + region.width && 
					region.y <= touchedPoint.y && 
					touchedPoint.y <= region.y + region.height
				) {
					return p;
				}
			}

			foreach(var p in m_data.OutputPoints) {
				var region = p.Region;
				if (region.x <= touchedPoint.x && 
					touchedPoint.x <= region.x + region.width && 
					region.y <= touchedPoint.y && 
					touchedPoint.y <= region.y + region.height
				) {
					return p;
				}
			}

			return null;
		}

		public Rect GetRect () {
			return m_baseRect;
		}

		public Vector2 GetPos () {
			return m_baseRect.position;
		}

		public int GetX () {
			return (int)m_baseRect.x;
		}

		public int GetY () {
			return (int)m_baseRect.y;
		}

		public int GetRightPos () {
			return (int)(m_baseRect.x + m_baseRect.width);
		}

		public int GetBottomPos () {
			return (int)(m_baseRect.y + m_baseRect.height);
		}

		public void SetPos (Vector2 position) {
			m_baseRect.position = position;
			m_data.X = position.x;
			m_data.Y = position.y;
		}

		public void MoveBy (Vector2 distance) {
			m_baseRect.position = m_baseRect.position + distance;
			m_data.X = m_data.X + distance.x;
			m_data.Y = m_data.Y + distance.y;
		}

		public void SetProgress (float val) {
			m_progress = val;
		}

		public void ShowProgress () {
			m_running = true;
		}

		public void HideProgress () {
			m_running = false;
		}

		public bool Conitains (Vector2 globalPos) {
			if (m_baseRect.Contains(globalPos)) {
				return true;
			}
			foreach (var point in m_data.OutputPoints) {
				if (point.GetGlobalPointRegion(this).Contains(globalPos)) {
					return true;
				}
			}
			return false;
		}

		public Model.ConnectionPointData FindConnectionPointByPosition (Vector2 globalPos) {

			foreach (var point in m_data.InputPoints) {
				if(!IsValidInputConnectionPoint(point)) {
					continue;
				}

				if (point.GetGlobalRegion(this).Contains(globalPos) || 
					point.GetGlobalPointRegion(this).Contains(globalPos)) 
				{
					return point;
				}
			}

			foreach (var point in m_data.OutputPoints) {
				if (point.GetGlobalRegion(this).Contains(globalPos) || 
					point.GetGlobalPointRegion(this).Contains(globalPos)) 
				{
					return point;
				}
			}

			return null;
		}

		public static void ShowTypeNamesMenu (string current, List<string> contents, Action<string> ExistSelected) {
			var menu = new GenericMenu();

			for (var i = 0; i < contents.Count; i++) {
				var type = contents[i];
				var selected = false;
				if (type == current) selected = true;

				menu.AddItem(
					new GUIContent(type),
					selected,
					() => {
						ExistSelected(type);
					}
				);
			}
			menu.ShowAsContext();
		}

		public static void ShowFilterKeyTypeMenu (string current, Action<string> Selected) {
			var menu = new GenericMenu();

			menu.AddDisabledItem(new GUIContent(current));

			menu.AddSeparator(string.Empty);

			for (var i = 0; i < TypeUtility.KeyTypes.Count; i++) {
				var type = TypeUtility.KeyTypes[i];
				if (type == current) continue;

				menu.AddItem(
					new GUIContent(type),
					false,
					() => {
						Selected(type);
					}
				);
			}
			menu.ShowAsContext();
		}
	}
}
