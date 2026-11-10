#!/usr/bin/env python3
"""Quarry_01 reference builder: deterministic terrain and visual design sheets.

Dependencies: numpy, scipy, pillow, matplotlib.
Run: python generate_quarry_reference.py
This is an offline reference generator, NOT a Unity scene exporter or a game-ready map.
"""
from __future__ import annotations
import json, math
from pathlib import Path
import numpy as np
from scipy.spatial import cKDTree
from PIL import Image
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.colors import LightSource, LinearSegmentedColormap
from matplotlib.patches import Circle, Patch
from matplotlib.lines import Line2D
from mpl_toolkits.mplot3d import Axes3D  # noqa

ROOT = Path(__file__).resolve().parent
CFG = json.loads((ROOT / 'quarry_01.json').read_text(encoding='utf-8'))
W = CFG['world']; Q = CFG['quarry']; R = CFG['roads']
SIZE = int(W['terrain_heightmap_resolution'])
L = W['size_x_m']; C = W['center_x_m']; CZ = W['center_z_m']
ROT = math.radians(Q['rotation_deg']); SX=Q['ellipse_scale_x']; SZ=Q['ellipse_scale_z']
plt.rcParams['font.family'] = 'DejaVu Sans'


def wriggle(angle):
    return Q['wall_variation_m'] * (0.68*np.sin(3*angle + .5) + .43*np.sin(5*angle - 1.1) + .28*np.sin(9*angle + 1.5))


def to_xz(radius, angle, follow_terraces=False):
    r = radius - wriggle(angle) if follow_terraces else radius
    u = r * np.cos(angle) * SX; v = r * np.sin(angle) * SZ
    return C + u*np.cos(ROT)-v*np.sin(ROT), CZ+u*np.sin(ROT)+v*np.cos(ROT)


def to_ra(x, z):
    dx=x-C; dz=z-C
    u=(dx*np.cos(ROT)+dz*np.sin(ROT))/SX
    v=(-dx*np.sin(ROT)+dz*np.cos(ROT))/SZ
    return np.hypot(u,v), np.arctan2(v,u)


def smoothstep01(x):
    t=np.clip(x,0,1)
    return t*t*(3-2*t)


def spiral_points(n=3300):
    s=R['main_spiral']; t=np.linspace(0,1,n)
    r=s['radius_start_m'] + (s['radius_end_m']-s['radius_start_m'])*t
    a=np.radians(s['start_angle_deg'])+2*np.pi*s['turns']*t
    x,z=to_xz(r,a)
    h=180*t
    return np.column_stack((x,z,h)), r, a


def spiral_angle_at_radius(r):
    s=R['main_spiral']
    t=(r-s['radius_start_m'])/(s['radius_end_m']-s['radius_start_m'])
    return math.radians(s['start_angle_deg'])+2*math.pi*s['turns']*t


def ring_points(item, n=1150):
    a=np.linspace(-np.pi,np.pi,n,endpoint=False)
    r=item['radius_m']; x,z=to_xz(r,a,True)
    # Tie the height of a closed bench road to the approaching spiral. A quarry
    # spiral can pass beside a ring for tens of metres, not at one isolated
    # crossing. Point-only matching creates fatal height discontinuities.
    sp,_,_=spiral_points()
    tree=cKDTree(sp[:,:2])
    proximity,near=tree.query(np.column_stack((x,z)))
    blend=1-smoothstep01(proximity/62.)
    h=item['height_m']*(1-blend)+sp[near,2]*blend
    return np.column_stack((x,z,h))


def terrain_surface():
    coords=np.linspace(0,L,SIZE,dtype=np.float32)
    xx,zz=np.meshgrid(coords,coords,indexing='xy')
    r,a=to_ra(xx,zz)
    rr=r+wriggle(a)*smoothstep01((r-120)/90)
    ht=np.zeros_like(rr)
    for threshold in Q['step_boundaries_radius_m']:
        ht+=Q['step_height_m']*smoothstep01((rr-threshold)/Q['step_transition_width_m'])
    # Modest material-level roughness; road corridor will be carved smooth.
    ht+= (0.48*np.sin(xx*.055)*np.sin(zz*.047)+0.29*np.sin(xx*.099+zz*.032))\
        *smoothstep01((r-100)/120)
    ht+=Q['outside_rolling_m'] * np.sin(xx/145)*np.cos(zz/175)*smoothstep01((r-570)/100)
    sp,_,_=spiral_points()
    roads=[('main_spiral',sp,R['main_spiral']['drive_width_m'],R['main_spiral']['shoulder_width_m'])]
    for item in R['ring_roads']:
        roads.append((item['id'],ring_points(item),item['drive_width_m'],item['shoulder_width_m']))
    allpts=[]; ally=[]; allcore=[]; allouter=[]
    for name,pts,width,shoulder in roads:
        allpts.append(pts[:,:2]); ally.append(pts[:,2]); allcore.append(np.full(len(pts),width/2))
        allouter.append(np.full(len(pts),width/2+shoulder))
    allpts=np.concatenate(allpts); ally=np.concatenate(ally)
    allcore=np.concatenate(allcore); allouter=np.concatenate(allouter)
    tree=cKDTree(allpts)
    dist,inds=tree.query(np.column_stack((xx.ravel(),zz.ravel())),workers=-1)
    dist=dist.reshape(xx.shape); inds=inds.reshape(xx.shape)
    f=1-smoothstep01((dist-allcore[inds])/(allouter[inds]-allcore[inds]))
    h=ht*(1-f)+ally[inds]*f
    # Terrain is at least 0m and no more than 210m.
    h=np.clip(h,0,W['height_max_m']).astype(np.float32)
    return coords,h,rr,roads


def poi_coord(p):
    if p.get('road_id'):
        road=next(v for v in R['ring_roads'] if v['id']==p['road_id'])
        rad=road['radius_m']; follow=True
    else:
        rad=p['radius_m'];follow=False
    x,z=to_xz(rad,math.radians(p['angle_deg']),follow)
    return float(x),float(z)


def save_heightmap(h):
    # 16-bit grayscale PNG: human-inspectable; height normalization is fixed 0..210 m.
    png=np.round(h/W['height_max_m']*65535).astype('<u2')
    Image.fromarray(png).save(ROOT/'Quarry_01_heightmap_16bit.png')
    # RAW16 for Unity Heightmap Import Raw. Row order is z increasing from 0 to +Z;
    # if a Unity import appears mirrored, use the import Flip Vertical setting.
    png.tofile(ROOT/'Quarry_01_heightmap_le.raw')
    # 8-bit preview only; not suitable for terrain import (visible quantization).
    preview=np.round(h/W['height_max_m']*255).astype('uint8')
    Image.fromarray(np.flipud(preview)).save(ROOT/'Quarry_01_heightmap_preview.png')


def draw_layout(coords,h,rr,roads):
    fig,ax=plt.subplots(figsize=(11,10),dpi=160)
    fig.patch.set_facecolor('#f5f2eb');ax.set_facecolor('#efece4')
    levels=[0,22,45,68,90,113,135,158,180,210]
    cmap=LinearSegmentedColormap.from_list('quarry', ['#565d62','#888c84','#b0a99a','#c9b99d','#ded7c3','#f3e8cd'])
    ax.imshow(h,extent=[0,L,0,L],origin='lower',cmap=cmap,vmin=0,vmax=210,alpha=.88)
    ax.contour(coords,coords,h,levels=levels,colors='#4b493e',alpha=.34,linewidths=.65)
    for name,pts,wd,sh in roads:
        col='#f3c05e' if name=='main_spiral' else '#4f9c98'
        ax.plot(pts[:,0],pts[:,1],color='#232b2a',lw=6.4 if name=='main_spiral' else 5.4,alpha=.9,zorder=9)
        ax.plot(pts[:,0],pts[:,1],color=col,lw=3.5 if name=='main_spiral' else 2.7,alpha=.98,zorder=10)
    poicol={'spawn':'#db633c','industrial':'#5a5da5','cover_zone':'#68964f','traction_zone':'#368bb8','fire_position':'#b84462'}
    for i,p in enumerate(CFG['points_of_interest']):
        x,z=poi_coord(p);col=poicol[p['type']]
        ax.scatter([x],[z],s=112,c=col,edgecolors='white',linewidths=1.8,zorder=20)
        ax.annotate(p['label'],(x,z),xytext=(7,7 if i%2 else -12),textcoords='offset points',fontsize=8.5,
                    weight='bold',color='#242c30',bbox=dict(boxstyle='round,pad=.22',facecolor='#f5f2eb',edgecolor='none',alpha=.91),zorder=21)
    ax.set_xlim(0,L);ax.set_ylim(0,L);ax.set_aspect('equal')
    ax.set_xticks(np.arange(0,L+1,250));ax.set_yticks(np.arange(0,L+1,250))
    ax.grid(True,color='#595c57',alpha=.12,lw=.65)
    ax.set_xlabel('X, метры (восток)',fontsize=11)
    ax.set_ylabel('Z, метры (север)',fontsize=11)
    ax.set_title('QUARRY 01  |  Генплан и игровые зоны',fontsize=17,weight='bold',loc='left',pad=18)
    ax.text(0,1532,'Версия 0.1 · 1500 × 1500 м · 8 уступов · основные дороги нанесены поверх рельефа',fontsize=9.5,color='#535c63')
    handles=[Line2D([0],[0],c='#f3c05e',lw=5,label='Основной серпантин'),
             Line2D([0],[0],c='#4f9c98',lw=5,label='Ярусные проезды'),
             *[Line2D([0],[0],marker='o',lw=0,markerfacecolor=c,markeredgecolor='white',markersize=9,label=k) for k,c in [('Старт',poicol['spawn']),('Огневая позиция',poicol['fire_position']),('Промзона',poicol['industrial']),('Укрытие',poicol['cover_zone']),('Грязь',poicol['traction_zone'])]]]
    ax.legend(handles=handles,loc='lower center',bbox_to_anchor=(.5,-.17),ncol=4,frameon=False,fontsize=8.5)
    fig.subplots_adjust(left=.1,right=.94,top=.9,bottom=.16)
    fig.savefig(ROOT/'Quarry_01_layout.png',facecolor=fig.get_facecolor());plt.close(fig)


def draw_elevation(coords,h,roads):
    fig,ax=plt.subplots(figsize=(12,7),dpi=155)
    ls=LightSource(azdeg=305,altdeg=42)
    colors=plt.cm.gist_earth(np.clip(h/210,0,1))
    shaded=ls.shade_rgb(colors[:,:,:3],h,vert_exag=6,dx=L/(SIZE-1),dy=L/(SIZE-1))
    ax.imshow(shaded,extent=[0,L,0,L],origin='lower')
    for name,pts,_,_ in roads:
        ax.plot(pts[:,0],pts[:,1],lw=2.3 if name=='main_spiral' else 1.6,
                c='#f2d198' if name=='main_spiral' else '#cfb9a5',alpha=.9)
    ax.set_aspect('equal');ax.axis('off')
    ax.set_title('Quarry_01 — рельеф и трассы | вид сверху',loc='left',fontsize=15,weight='bold',pad=12)
    fig.tight_layout();fig.savefig(ROOT/'Quarry_01_relief_topview.png',bbox_inches='tight',facecolor='white');plt.close(fig)


def draw_3d(h,roads):
    step=5
    h2=h[::step,::step];s=np.linspace(0,L,len(h2))
    x,z=np.meshgrid(s,s)
    fig=plt.figure(figsize=(13,8),dpi=145,facecolor='#f2f4f3')
    ax=fig.add_subplot(111,projection='3d',computed_zorder=False)
    ax.set_facecolor('#f2f4f3')
    cmap=LinearSegmentedColormap.from_list('mine',['#55565a','#737575','#a4a29c','#c2bcb0','#d5c9ae','#e6dbca'])
    ax.plot_surface(x,z,h2,facecolors=cmap(h2/210),rstride=1,cstride=1,linewidth=0,antialiased=False,shade=True,alpha=1)
    # Gently offset path above ground to remain legible.
    for name,pts,_,_ in roads:
        ax.plot(pts[:,0],pts[:,1],pts[:,2]+4.2,c='#f3c66d' if name=='main_spiral' else '#54b5ae',
                lw=2.0 if name=='main_spiral' else 1.2,alpha=.98,zorder=20)
    ax.view_init(elev=42,azim=-69)
    ax.set_box_aspect((1,1,.42),zoom=.88)
    ax.set_xlim(0,L);ax.set_ylim(0,L);ax.set_zlim(0,290)
    ax.set_xticks([]);ax.set_yticks([]);ax.set_zticks([])
    ax.set_xlabel('X');ax.set_ylabel('Z');ax.set_zlabel('Высота')
    ax.set_title('QUARRY 01  |  Объёмная схема рельефа',fontsize=18,weight='bold',pad=16)
    fig.subplots_adjust(left=0,right=1,bottom=.03,top=.93)
    fig.savefig(ROOT/'Quarry_01_3d_view.png',facecolor=fig.get_facecolor());plt.close(fig)


def draw_profile(h,coords):
    # E-W vertical slice through center. Terrain height is driven by stepped pit and ramps.
    ii=np.argmin(np.abs(coords-CZ))
    fig,ax=plt.subplots(figsize=(12,3.8),dpi=155)
    fig.patch.set_facecolor('#f7f6f3');ax.set_facecolor('#f7f6f3')
    ax.fill_between(coords,h[ii,:],0,color='#a08d75',alpha=.32)
    ax.plot(coords,h[ii,:],c='#5b574d',lw=2.2)
    ax.set_xlim(0,L);ax.set_ylim(0,210)
    ax.set_yticks([0,45,90,135,180]);ax.grid(alpha=.18)
    ax.set_title('Поперечный профиль З–В (Z = 750 м)',fontsize=14,weight='bold',loc='left',pad=10)
    ax.set_xlabel('X, м');ax.set_ylabel('Высота, м')
    fig.tight_layout();fig.savefig(ROOT/'Quarry_01_cross_section.png',facecolor=fig.get_facecolor());plt.close(fig)


def check_roads(h,coords,roads):
    from scipy.ndimage import map_coordinates
    report=[]
    for name,pts,width,shoulder in roads:
        # Sample actual carved height map and calculate grades over 10 m intervals;
        # avoid unstable derivative due raster sampling at < 1 m intervals.
        xy=pts[:,:2]
        dist=np.r_[0,np.cumsum(np.linalg.norm(np.diff(xy,axis=0),axis=1))]
        chain=np.arange(0,dist[-1],10.)
        x=np.interp(chain,dist,pts[:,0]);z=np.interp(chain,dist,pts[:,1])
        actual=map_coordinates(h,[z/(L/(SIZE-1)),x/(L/(SIZE-1))],order=1,mode='nearest')
        grade=100*np.abs(np.diff(actual))/10
        report.append({'name':name,'length_m':round(float(dist[-1])), 'max_centerline_grade_pct':round(float(np.max(grade)),1),'p95_centerline_grade_pct':round(float(np.percentile(grade,95)),1)})
    (ROOT/'road_checks.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    return report


def main():
    print('Generating Quarry_01, resolution',SIZE)
    coords,h,rr,roads=terrain_surface()
    save_heightmap(h)
    draw_layout(coords,h,rr,roads)
    draw_elevation(coords,h,roads)
    draw_3d(h,roads)
    draw_profile(h,coords)
    checks=check_roads(h,coords,roads)
    print('Height range:',float(h.min()),float(h.max()))
    for item in checks:print(item)
    print('Complete:',ROOT)

if __name__=='__main__':main()
