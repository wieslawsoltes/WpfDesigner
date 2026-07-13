// Copyright (c) 2014 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Windows;
using System.Windows.Input;

namespace ICSharpCode.WpfDesign.Designer.Services
{
	sealed class PointerTool : IPointerTool
	{
		internal static readonly PointerTool Instance = new PointerTool();
		
		public Cursor Cursor {
			get { return null; }
		}
		
		public void Activate(IDesignPanel designPanel)
		{
			designPanel.MouseDown += OnMouseDown;
		}
		
		public void Deactivate(IDesignPanel designPanel)
		{
			designPanel.MouseDown -= OnMouseDown;
		}
		
		void OnMouseDown(object sender, MouseButtonEventArgs e)
		{
			IDesignPanel designPanel = (IDesignPanel)sender;
			Point pointerPosition = e.GetPosition(designPanel);
			DesignPanelHitTestResult result = HitTest(designPanel, pointerPosition);
			if (result.ModelHit != null) {
				IHandlePointerToolMouseDown b = result.ModelHit.GetBehavior<IHandlePointerToolMouseDown>();
				if (b != null) {
					b.HandleSelectionMouseDown(designPanel, e, result);
				}
				if (!e.Handled) {
					if (e.ChangedButton == MouseButton.Left && MouseGestureBase.IsOnlyButtonPressed(e, MouseButton.Left)) {
						e.Handled = true;
						PointerToolGesture gesture = TryStartGesture(
							designPanel,
							pointerPosition,
							e.ClickCount,
							SelectionTypes.Auto,
							result);
						if (gesture != null)
							new RoutedPointerToolGesture(gesture).Start(designPanel, e);
					}
				}
			}
		}

		public IPointerToolGesture TryStartGesture(
			IDesignPanel designPanel,
			Point pointerPosition,
			int clickCount,
			SelectionTypes selectionType)
		{
			if (designPanel == null)
				throw new ArgumentNullException("designPanel");
			if (clickCount < 1)
				throw new ArgumentOutOfRangeException("clickCount");
			if (!IsFinite(pointerPosition))
				return null;

			return TryStartGesture(
				designPanel,
				pointerPosition,
				clickCount,
				selectionType,
				HitTest(designPanel, pointerPosition));
		}

		static DesignPanelHitTestResult HitTest(IDesignPanel designPanel, Point pointerPosition)
		{
			return designPanel.HitTest(
				pointerPosition,
				false,
				true,
				HitTestType.ElementSelection);
		}

		static PointerToolGesture TryStartGesture(
			IDesignPanel designPanel,
			Point pointerPosition,
			int clickCount,
			SelectionTypes selectionType,
			DesignPanelHitTestResult result)
		{
			if (result.ModelHit == null)
				return null;

			ISelectionService selectionService = designPanel.Context.Services.Selection;
			bool setSelectionIfNotMoving = selectionService.IsComponentSelected(result.ModelHit);
			if (!setSelectionIfNotMoving) {
				selectionService.SetSelectedComponents(
					new DesignItem[] { result.ModelHit },
					selectionType);
			}

			if (!selectionService.IsComponentSelected(result.ModelHit))
				return null;

			return new PointerToolGesture(
				result.ModelHit,
				pointerPosition,
				clickCount == 2,
				setSelectionIfNotMoving,
				selectionType);
		}

		static bool IsFinite(Point point)
		{
			return !double.IsNaN(point.X)
				&& !double.IsInfinity(point.X)
				&& !double.IsNaN(point.Y)
				&& !double.IsInfinity(point.Y);
		}

		sealed class PointerToolGesture : IPointerToolGesture
		{
			readonly Point startPoint;
			readonly bool isDoubleClick;
			readonly bool setSelectionIfNotMoving;
			readonly SelectionTypes selectionType;
			readonly MoveLogic moveLogic;
			bool isActive = true;
			bool hasMoved;

			internal PointerToolGesture(
				DesignItem hitItem,
				Point startPoint,
				bool isDoubleClick,
				bool setSelectionIfNotMoving,
				SelectionTypes selectionType)
			{
				this.startPoint = startPoint;
				this.isDoubleClick = isDoubleClick;
				this.setSelectionIfNotMoving = setSelectionIfNotMoving;
				this.selectionType = selectionType;
				moveLogic = new MoveLogic(hitItem);
			}

			public DesignItem HitItem {
				get { return moveLogic.ClickedOn; }
			}

			public bool IsActive {
				get { return isActive; }
			}

			public bool HasMoved {
				get { return hasMoved; }
			}

			public bool Move(Point pointerPosition)
			{
				if (!isActive || !IsFinite(pointerPosition))
					return false;

				if (!hasMoved) {
					Vector delta = pointerPosition - startPoint;
					if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
						&& Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
						return false;

					moveLogic.Start(startPoint);
					if (moveLogic.Operation == null)
						return false;
					hasMoved = true;
				}

				moveLogic.Move(pointerPosition);
				return true;
			}

			public void Complete()
			{
				if (!isActive)
					return;

				if (!hasMoved) {
					if (isDoubleClick) {
						moveLogic.HandleDoubleClick();
					} else if (setSelectionIfNotMoving) {
						HitItem.Services.Selection.SetSelectedComponents(
							new DesignItem[] { HitItem },
							selectionType);
					}
				}

				moveLogic.Stop();
				isActive = false;
			}

			public void Cancel()
			{
				if (!isActive)
					return;

				moveLogic.Cancel();
				isActive = false;
			}
		}

		sealed class RoutedPointerToolGesture : MouseGestureBase
		{
			readonly IPointerToolGesture gesture;

			internal RoutedPointerToolGesture(IPointerToolGesture gesture)
			{
				this.gesture = gesture;
			}

			protected override void OnMouseMove(object sender, MouseEventArgs e)
			{
				gesture.Move(e.GetPosition(designPanel));
			}

			protected override void OnMouseUp(object sender, MouseButtonEventArgs e)
			{
				gesture.Complete();
				Stop();
			}

			protected override void OnStopped()
			{
				gesture.Cancel();
			}
		}
	}
}
