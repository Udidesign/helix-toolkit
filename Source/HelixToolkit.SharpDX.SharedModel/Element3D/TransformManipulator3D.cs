﻿/*
The MIT License (MIT)
Copyright (c) 2018 Helix Toolkit contributors
*/
using SharpDX;
using SharpDX.Direct3D11;
using System.Collections.Generic;
using System.Linq;

#if NETFX_CORE
using Windows.UI.Xaml;
using Media = Windows.UI;
namespace HelixToolkit.UWP
#else
using System.Windows;
using Media3D = System.Windows.Media.Media3D;
using Media = System.Windows.Media;
namespace HelixToolkit.Wpf.SharpDX
#endif
{
    using Model.Scene;
    using System;
    using System.Diagnostics;

    public class TransformManipulator3D : GroupElement3D
    {
        private static readonly Geometry3D TranslationXGeometry;
        private static readonly Geometry3D RotationXGeometry;
        private static readonly Geometry3D ScalingGeometry;

        static TransformManipulator3D()
        {
            var bd = new MeshBuilder();
            float arrowLength = 1.5f;
            bd.AddArrow(Vector3.UnitX * arrowLength, new Vector3(1.2f * arrowLength, 0, 0), 0.08, 4, 12);
            bd.AddCylinder(Vector3.Zero, Vector3.UnitX * arrowLength, 0.04, 12);
            TranslationXGeometry = bd.ToMesh();

            bd = new MeshBuilder();
            var circle = MeshBuilder.GetCircle(32, true);
            var path = circle.Select(x => new Vector3(0, x.X, x.Y)).ToArray();
            bd.AddTube(path, 0.06, 8, true);
            RotationXGeometry = bd.ToMesh();

            bd = new MeshBuilder();
            bd.AddBox(Vector3.UnitX * 0.8f, 0.15, 0.15, 0.15);
            bd.AddCylinder(Vector3.Zero, Vector3.UnitX * 0.8f, 0.02, 4);
            ScalingGeometry = bd.ToMesh();

            TranslationXGeometry.OctreeParameter.MinimumOctantSize = 0.01f;
            TranslationXGeometry.UpdateOctree();

            RotationXGeometry.OctreeParameter.MinimumOctantSize = 0.01f;
            RotationXGeometry.UpdateOctree();

            ScalingGeometry.OctreeParameter.MinimumOctantSize = 0.01f;
            ScalingGeometry.UpdateOctree();
        }
        #region Dependency Properties
        public Element3D Target
        {
            get { return (Element3D)GetValue(TargetProperty); }
            set { SetValue(TargetProperty, value); }
        }


        public static readonly DependencyProperty TargetProperty =
            DependencyProperty.Register("Target", typeof(Element3D), typeof(TransformManipulator3D), new PropertyMetadata(null, (d, e) =>
            {
                (d as TransformManipulator3D).OnTargetChanged(e.NewValue as Element3D);
            }));



        public bool EnableScaling
        {
            get { return (bool)GetValue(EnableScalingProperty); }
            set { SetValue(EnableScalingProperty, value); }
        }

        public static readonly DependencyProperty EnableScalingProperty =
            DependencyProperty.Register("EnableScaling", typeof(bool), typeof(TransformManipulator3D), new PropertyMetadata(true, (d, e)=> 
            {
                (d as TransformManipulator3D).scaleGroup.IsRendering = (bool)e.NewValue;
            }));


        public bool EnableTranslation
        {
            get { return (bool)GetValue(EnableTranslationProperty); }
            set { SetValue(EnableTranslationProperty, value); }
        }

        public static readonly DependencyProperty EnableTranslationProperty =
            DependencyProperty.Register("EnableTranslation", typeof(bool), typeof(TransformManipulator3D), new PropertyMetadata(true, (d, e) =>
            {
                (d as TransformManipulator3D).translationGroup.IsRendering = (bool)e.NewValue;
            }));

        public bool EnableRotation
        {
            get { return (bool)GetValue(EnableRotationProperty); }
            set { SetValue(EnableRotationProperty, value); }
        }

        public static readonly DependencyProperty EnableRotationProperty =
            DependencyProperty.Register("EnableRotation", typeof(bool), typeof(TransformManipulator3D), new PropertyMetadata(true, (d, e) =>
            {
                (d as TransformManipulator3D).rotationGroup.IsRendering = (bool)e.NewValue;
            }));


        public bool EnableXRayGrid
        {
            get { return (bool)GetValue(EnableXRayGridProperty); }
            set { SetValue(EnableXRayGridProperty, value); }
        }

        public static readonly DependencyProperty EnableXRayGridProperty =
            DependencyProperty.Register("EnableXRayGrid", typeof(bool), typeof(TransformManipulator3D), new PropertyMetadata(true, (d, e) =>
            {
                (d as TransformManipulator3D).xrayEffect.IsRendering = (bool)e.NewValue;
            }));
        #endregion
        private readonly MeshGeometryModel3D translationX, translationY, translationZ;
        private readonly MeshGeometryModel3D rotationX, rotationY, rotationZ;
        private readonly MeshGeometryModel3D scaleX, scaleY, scaleZ;
        private readonly GroupModel3D translationGroup, rotationGroup, scaleGroup;
        private readonly Element3D xrayEffect;

        private Matrix translationMatrix = Matrix.Identity;
        private Matrix rotationMatrix = Matrix.Identity;
        private Matrix scaleMatrix = Matrix.Identity;
        private Matrix targetMatrix = Matrix.Identity;

        private Element3D target;
        private Viewport3DX currentViewport;
        private Vector3 lastHitPosWS;
        private Vector3 normal;

        private Vector3 direction;
        private Vector3 currentHit;
        private bool isCaptured = false;

        private enum ManipulationType
        {
            None, TranslationX, TranslationY, TranslationZ, RotationX, RotationY, RotationZ, ScaleX, ScaleY, ScaleZ
        }

        private ManipulationType manipulationType = ManipulationType.None;

        public TransformManipulator3D()
        {
            var rotationYMatrix = Matrix.RotationZ((float)Math.PI / 2);
            var rotationZMatrix = Matrix.RotationY(-(float)Math.PI / 2);

            #region Translation Models
            translationX = new MeshGeometryModel3D() { Geometry = TranslationXGeometry, Material = DiffuseMaterials.Red, CullMode = CullMode.Back, PostEffects = "ManipulatorXRayGrid" };
            translationY = new MeshGeometryModel3D() { Geometry = TranslationXGeometry, Material = DiffuseMaterials.Green, CullMode = CullMode.Back, PostEffects = "ManipulatorXRayGrid" };
            translationZ = new MeshGeometryModel3D() { Geometry = TranslationXGeometry, Material = DiffuseMaterials.Blue, CullMode = CullMode.Back, PostEffects = "ManipulatorXRayGrid" };
#if !NETFX_CORE
            translationY.Transform = new Media3D.MatrixTransform3D(rotationYMatrix.ToMatrix3D());
            translationZ.Transform = new Media3D.MatrixTransform3D(rotationZMatrix.ToMatrix3D());
            translationX.Mouse3DDown += Translation_Mouse3DDown;
            translationY.Mouse3DDown += Translation_Mouse3DDown;
            translationZ.Mouse3DDown += Translation_Mouse3DDown;
            translationX.Mouse3DMove += Translation_Mouse3DMove;
            translationY.Mouse3DMove += Translation_Mouse3DMove;
            translationZ.Mouse3DMove += Translation_Mouse3DMove;
            translationX.Mouse3DUp += Manipulation_Mouse3DUp;
            translationY.Mouse3DUp += Manipulation_Mouse3DUp;
            translationZ.Mouse3DUp += Manipulation_Mouse3DUp;
#else
            translationY.Transform3D = rotationYMatrix;
            translationZ.Transform3D = rotationZMatrix;
            translationX.OnMouse3DDown += Translation_Mouse3DDown;
            translationY.OnMouse3DDown += Translation_Mouse3DDown;
            translationZ.OnMouse3DDown += Translation_Mouse3DDown;
            translationX.OnMouse3DMove += Translation_Mouse3DMove;
            translationY.OnMouse3DMove += Translation_Mouse3DMove;
            translationZ.OnMouse3DMove += Translation_Mouse3DMove;
            translationX.OnMouse3DUp += Manipulation_Mouse3DUp;
            translationY.OnMouse3DUp += Manipulation_Mouse3DUp;
            translationZ.OnMouse3DUp += Manipulation_Mouse3DUp;
#endif

            translationGroup = new GroupModel3D();
            translationGroup.Children.Add(translationX);
            translationGroup.Children.Add(translationY);
            translationGroup.Children.Add(translationZ);
            Children.Add(translationGroup);
            #endregion
            #region Rotation Models
            rotationX = new MeshGeometryModel3D() { Geometry = RotationXGeometry, Material = DiffuseMaterials.Red, CullMode = CullMode.Back, PostEffects = "ManipulatorXRayGrid" };
            rotationY = new MeshGeometryModel3D() { Geometry = RotationXGeometry, Material = DiffuseMaterials.Green, CullMode = CullMode.Back, PostEffects = "ManipulatorXRayGrid" };
            rotationZ = new MeshGeometryModel3D() { Geometry = RotationXGeometry, Material = DiffuseMaterials.Blue, CullMode = CullMode.Back, PostEffects = "ManipulatorXRayGrid" };
#if !NETFX_CORE
            rotationY.Transform = new Media3D.MatrixTransform3D(rotationYMatrix.ToMatrix3D());
            rotationZ.Transform = new Media3D.MatrixTransform3D(rotationZMatrix.ToMatrix3D());
            rotationX.Mouse3DDown += Rotation_Mouse3DDown;
            rotationY.Mouse3DDown += Rotation_Mouse3DDown;
            rotationZ.Mouse3DDown += Rotation_Mouse3DDown;
            rotationX.Mouse3DMove += Rotation_Mouse3DMove;
            rotationY.Mouse3DMove += Rotation_Mouse3DMove;
            rotationZ.Mouse3DMove += Rotation_Mouse3DMove;
            rotationX.Mouse3DUp += Manipulation_Mouse3DUp;
            rotationY.Mouse3DUp += Manipulation_Mouse3DUp;
            rotationZ.Mouse3DUp += Manipulation_Mouse3DUp;
#else
            rotationY.Transform3D = rotationYMatrix;
            rotationZ.Transform3D = rotationZMatrix;
            rotationX.OnMouse3DDown += Rotation_Mouse3DDown;
            rotationY.OnMouse3DDown += Rotation_Mouse3DDown;
            rotationZ.OnMouse3DDown += Rotation_Mouse3DDown;
            rotationX.OnMouse3DMove += Rotation_Mouse3DMove;
            rotationY.OnMouse3DMove += Rotation_Mouse3DMove;
            rotationZ.OnMouse3DMove += Rotation_Mouse3DMove;
            rotationX.OnMouse3DUp += Manipulation_Mouse3DUp;
            rotationY.OnMouse3DUp += Manipulation_Mouse3DUp;
            rotationZ.OnMouse3DUp += Manipulation_Mouse3DUp;
#endif

            rotationGroup = new GroupModel3D();
            rotationGroup.Children.Add(rotationX);
            rotationGroup.Children.Add(rotationY);
            rotationGroup.Children.Add(rotationZ);
            Children.Add(rotationGroup);
            #endregion
            #region Scaling Models
            scaleX = new MeshGeometryModel3D() { Geometry = ScalingGeometry, Material = DiffuseMaterials.Red, CullMode = CullMode.Back, PostEffects = "ManipulatorXRayGrid" };
            scaleY = new MeshGeometryModel3D() { Geometry = ScalingGeometry, Material = DiffuseMaterials.Green, CullMode = CullMode.Back, PostEffects = "ManipulatorXRayGrid" };
            scaleZ = new MeshGeometryModel3D() { Geometry = ScalingGeometry, Material = DiffuseMaterials.Blue, CullMode = CullMode.Back, PostEffects = "ManipulatorXRayGrid" };
#if !NETFX_CORE
            scaleY.Transform = new Media3D.MatrixTransform3D(rotationYMatrix.ToMatrix3D());
            scaleZ.Transform = new Media3D.MatrixTransform3D(rotationZMatrix.ToMatrix3D());
            scaleX.Mouse3DDown += Scaling_Mouse3DDown;
            scaleY.Mouse3DDown += Scaling_Mouse3DDown;
            scaleZ.Mouse3DDown += Scaling_Mouse3DDown;
            scaleX.Mouse3DMove += Scaling_Mouse3DMove;
            scaleY.Mouse3DMove += Scaling_Mouse3DMove;
            scaleZ.Mouse3DMove += Scaling_Mouse3DMove;
            scaleX.Mouse3DUp += Manipulation_Mouse3DUp;
            scaleY.Mouse3DUp += Manipulation_Mouse3DUp;
            scaleZ.Mouse3DUp += Manipulation_Mouse3DUp;
#else
            scaleY.Transform3D = rotationYMatrix;
            scaleZ.Transform3D = rotationZMatrix;
            scaleX.OnMouse3DDown += Scaling_Mouse3DDown;
            scaleY.OnMouse3DDown += Scaling_Mouse3DDown;
            scaleZ.OnMouse3DDown += Scaling_Mouse3DDown;
            scaleX.OnMouse3DMove += Scaling_Mouse3DMove;
            scaleY.OnMouse3DMove += Scaling_Mouse3DMove;
            scaleZ.OnMouse3DMove += Scaling_Mouse3DMove;
            scaleX.OnMouse3DUp += Manipulation_Mouse3DUp;
            scaleY.OnMouse3DUp += Manipulation_Mouse3DUp;
            scaleZ.OnMouse3DUp += Manipulation_Mouse3DUp;
#endif

            scaleGroup = new GroupModel3D();
            scaleGroup.Children.Add(scaleX);
            scaleGroup.Children.Add(scaleY);
            scaleGroup.Children.Add(scaleZ);
            Children.Add(scaleGroup);
            #endregion
            xrayEffect = new PostEffectMeshXRayGrid()
            {
                EffectName = "ManipulatorXRayGrid",
                DimmingFactor = 0.5, BlendingFactor = 0.8,
                GridDensity = 4, GridColor = Media.Colors.Gray
            };
            (xrayEffect.SceneNode as NodePostEffectXRayGrid).XRayDrawingPassName = DefaultPassNames.EffectMeshDiffuseXRayGridP3;
            Children.Add(xrayEffect);
        }
        #region Handle Translation
        private void Translation_Mouse3DDown(object sender, MouseDown3DEventArgs e)
        {
            if (target == null)
            {
                return;
            }
            if (e.HitTestResult.ModelHit == translationX)
            {
                manipulationType = ManipulationType.TranslationX;
                direction = Vector3.UnitX;
            }
            else if (e.HitTestResult.ModelHit == translationY)
            {
                manipulationType = ManipulationType.TranslationY;
                direction = Vector3.UnitY;
            }
            else if (e.HitTestResult.ModelHit == translationZ)
            {
                manipulationType = ManipulationType.TranslationZ;
                direction = Vector3.UnitZ;
            }
            else
            {
                manipulationType = ManipulationType.None;
                isCaptured = false;
                return;
            }
            currentViewport = e.Viewport;
            var cameraNormal = Vector3.Normalize(e.Viewport.Camera.CameraInternal.LookDirection);
            this.lastHitPosWS = e.HitTestResult.PointHit;
            var up = Vector3.Cross(cameraNormal, direction);
            normal = Vector3.Cross(up, direction);
            var hit = currentViewport.UnProjectOnPlane(e.Position.ToVector2(), lastHitPosWS, normal);
            if (hit.HasValue)
            {
                currentHit = hit.Value;
                isCaptured = true;
            }
        }

        private void Translation_Mouse3DMove(object sender, MouseMove3DEventArgs e)
        {
            if (!isCaptured)
            {
                return;
            }
            var hit = currentViewport.UnProjectOnPlane(e.Position.ToVector2(), lastHitPosWS, normal);
            if (hit.HasValue)
            {
                var moveDir = hit.Value - currentHit;
                currentHit = hit.Value;
                switch (manipulationType)
                {
                    case ManipulationType.TranslationX:
                        translationMatrix.TranslationVector += new Vector3(moveDir.X, 0, 0);
                        break;
                    case ManipulationType.TranslationY:
                        translationMatrix.TranslationVector += new Vector3(0, moveDir.Y, 0);
                        break;
                    case ManipulationType.TranslationZ:
                        translationMatrix.TranslationVector += new Vector3(0, 0, moveDir.Z);
                        break;
                }

#if !NETFX_CORE
                Transform = new Media3D.MatrixTransform3D(translationMatrix.ToMatrix3D());
#else
                Transform3D = translationMatrix;
#endif
                OnUpdateTargetMatrix();
            }


        }
        #endregion

        #region Handle Rotation
        private void Rotation_Mouse3DDown(object sender, MouseDown3DEventArgs e)
        {
            if (target == null)
            {
                return;
            }
            if (e.HitTestResult.ModelHit == rotationX)
            {
                manipulationType = ManipulationType.RotationX;
                direction = new Vector3(1, 0, 0);
            }
            else if (e.HitTestResult.ModelHit == rotationY)
            {
                manipulationType = ManipulationType.RotationY;
                direction = new Vector3(0, 1, 0);
            }
            else if (e.HitTestResult.ModelHit == rotationZ)
            {
                manipulationType = ManipulationType.RotationZ;
                direction = new Vector3(0, 0, 1);
            }
            else
            {
                manipulationType = ManipulationType.None;
                isCaptured = false;
                return;
            }
            currentViewport = e.Viewport;
            normal = Vector3.Normalize(e.Viewport.Camera.CameraInternal.LookDirection);
            this.lastHitPosWS = e.HitTestResult.PointHit;
            //var up = Vector3.Cross(cameraNormal, direction);
            //normal = Vector3.Cross(up, direction);
            var hit = currentViewport.UnProjectOnPlane(e.Position.ToVector2(), lastHitPosWS, normal);
            if (hit.HasValue)
            {
                currentHit = hit.Value;
                isCaptured = true;
            }
        }

        private void Rotation_Mouse3DMove(object sender, MouseMove3DEventArgs e)
        {
            if (!isCaptured)
            {
                return;
            }
            var hit = currentViewport.UnProjectOnPlane(e.Position.ToVector2(), lastHitPosWS, normal);
            var position = this.translationMatrix.TranslationVector;
            if (hit.HasValue)
            {
                var v = Vector3.Normalize(currentHit - position);
                var u = Vector3.Normalize(hit.Value - position);
                var currentAxis = Vector3.Cross(u, v);
                var axis = Vector3.UnitX;
                currentHit = hit.Value;
                switch (manipulationType)
                {
                    case ManipulationType.RotationX:
                        axis = Vector3.UnitX;
                        break;
                    case ManipulationType.RotationY:
                        axis = Vector3.UnitY;
                        break;
                    case ManipulationType.RotationZ:
                        axis = Vector3.UnitZ;
                        break;
                }
                var sign = -Vector3.Dot(axis, currentAxis);
                var theta = (float)(Math.Sign(sign) * Math.Asin(currentAxis.Length()));
                switch (manipulationType)
                {
                    case ManipulationType.RotationX:
                        rotationMatrix *= Matrix.RotationX(theta);
                        break;
                    case ManipulationType.RotationY:
                        rotationMatrix *= Matrix.RotationY(theta);
                        break;
                    case ManipulationType.RotationZ:
                        rotationMatrix *= Matrix.RotationZ(theta);
                        break;
                }
                OnUpdateTargetMatrix();
            }
        }
        #endregion

        #region Handle Scaling
        private void Scaling_Mouse3DDown(object sender, MouseDown3DEventArgs e)
        {
            if (target == null)
            {
                return;
            }
            if (e.HitTestResult.ModelHit == scaleX)
            {
                manipulationType = ManipulationType.ScaleX;
                direction = Vector3.UnitX;
            }
            else if (e.HitTestResult.ModelHit == scaleY)
            {
                manipulationType = ManipulationType.ScaleY;
                direction = Vector3.UnitY;
            }
            else if (e.HitTestResult.ModelHit == scaleZ)
            {
                manipulationType = ManipulationType.ScaleZ;
                direction = Vector3.UnitZ;
            }
            else
            {
                manipulationType = ManipulationType.None;
                isCaptured = false;
                return;
            }
            currentViewport = e.Viewport;
            var cameraNormal = Vector3.Normalize(e.Viewport.Camera.CameraInternal.LookDirection);
            this.lastHitPosWS = e.HitTestResult.PointHit;
            var up = Vector3.Cross(cameraNormal, direction);
            normal = Vector3.Cross(up, direction);
            var hit = currentViewport.UnProjectOnPlane(e.Position.ToVector2(), lastHitPosWS, normal);
            if (hit.HasValue)
            {
                currentHit = hit.Value;
                isCaptured = true;
            }
        }

        private void Scaling_Mouse3DMove(object sender, MouseMove3DEventArgs e)
        {
            if (!isCaptured)
            {
                return;
            }
            var hit = currentViewport.UnProjectOnPlane(e.Position.ToVector2(), lastHitPosWS, normal);
            if (hit.HasValue)
            {
                var moveDir = hit.Value - currentHit;
                currentHit = hit.Value;
                switch (manipulationType)
                {
                    case ManipulationType.ScaleX:
                        scaleMatrix.M11 += moveDir.X;
                        break;
                    case ManipulationType.ScaleY:
                        scaleMatrix.M22 += moveDir.Y;
                        break;
                    case ManipulationType.ScaleZ:
                        scaleMatrix.M33 += moveDir.Z;
                        break;
                }
                OnUpdateTargetMatrix();
            }
        }
        #endregion

        private void Manipulation_Mouse3DUp(object sender, MouseUp3DEventArgs e)
        {
            manipulationType = ManipulationType.None;
            isCaptured = false;
        }

        private void ResetTransforms()
        {
            translationMatrix = scaleMatrix = rotationMatrix = targetMatrix = Matrix.Identity;
#if !NETFX_CORE
            Transform = new Media3D.MatrixTransform3D(translationMatrix.ToMatrix3D());
#else
            this.Transform3D = translationMatrix;
#endif
        }

        private void OnTargetChanged(Element3D target)
        {
            Debug.WriteLine("OnTargetChanged");
            this.target = target;
            if (target == null)
            {
                ResetTransforms();
            }
            var m = Matrix.Identity;
#if !NETFX_CORE
            if (target.Transform != null)
            { m = target.Transform.Value.ToMatrix(); }
#else
            m = target.Transform3D;
#endif
            m.Decompose(out Vector3 scale, out Quaternion rotation, out Vector3 translation);
            scaleMatrix = Matrix.Scaling(scale);
            rotationMatrix = Matrix.RotationQuaternion(rotation);
            translationMatrix = Matrix.Translation(translation);
#if !NETFX_CORE
            Transform = new Media3D.MatrixTransform3D(translationMatrix.ToMatrix3D());
#else
            Transform3D = m;
#endif
            OnUpdateTargetMatrix();
        }

        private void OnUpdateTargetMatrix()
        {
            if (target == null)
            {
                return;
            }
            targetMatrix = scaleMatrix * rotationMatrix * translationMatrix;
#if !NETFX_CORE
            target.Transform = new Media3D.MatrixTransform3D(targetMatrix.ToMatrix3D());
#else
            target.Transform3D = targetMatrix;
#endif
        }

        protected override SceneNode OnCreateSceneNode()
        {
            return new AlwaysHitGroupNode(this);
        }

        private sealed class AlwaysHitGroupNode : GroupNode
        {
            private readonly HashSet<object> models = new HashSet<object>();
            private readonly TransformManipulator3D manipulator;
            public AlwaysHitGroupNode(TransformManipulator3D manipulator)
            {
                this.manipulator = manipulator;
            }

            protected override bool OnAttach(IRenderHost host)
            {
                models.Add(manipulator.translationX);
                models.Add(manipulator.translationY);
                models.Add(manipulator.translationZ);
                models.Add(manipulator.rotationX);
                models.Add(manipulator.rotationY);
                models.Add(manipulator.rotationZ);
                models.Add(manipulator.scaleX);
                models.Add(manipulator.scaleY);
                models.Add(manipulator.scaleZ);
                return base.OnAttach(host);
            }
            protected override bool OnHitTest(RenderContext context, Matrix totalModelMatrix, ref Ray ray, ref List<HitTestResult> hits)
            {
                //Set hit distance to 0 so event manipulator is inside the model, hit test still works
                if (base.OnHitTest(context, totalModelMatrix, ref ray, ref hits))
                {
                    if (hits.Count > 0)
                    {
                        HitTestResult res = new HitTestResult() { Distance = float.MaxValue };
                        foreach (var hit in hits)
                        {
                            if (models.Contains(hit.ModelHit))
                            {
                                if (hit.Distance < res.Distance)
                                { res = hit; }
                            }
                        }
                        res.Distance = 0;
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
    }
}
